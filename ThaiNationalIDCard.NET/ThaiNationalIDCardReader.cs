using PCSC;
using PCSC.Exceptions;
using PCSC.Iso7816;
using PCSC.Utils;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ThaiNationalIDCard.NET.Interfaces;
using ThaiNationalIDCard.NET.Models;

namespace ThaiNationalIDCard.NET
{
    public class ThaiNationalIDCardReader
    {
        private readonly ISCardContext context;
        private readonly ISCardReader reader;

        private SCardError error;
        private IntPtr intPtr;
        private IThaiNationalIDCardAPDUCommand apdu;

        public ThaiNationalIDCardReader()
        {
            context = ContextFactory.Instance.Establish(SCardScope.System);
            reader = new SCardReader(context);
            // keep previous default type if present in solution; if not, consumer can set apdu externally
            apdu = new ThaiNationalIDCardAPDUCommandType02();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        // Allow injecting a custom APDU provider
        public void SetApduProvider(IThaiNationalIDCardAPDUCommand apduProvider)
        {
            apdu = apduProvider ?? throw new ArgumentNullException(nameof(apduProvider));
        }

        public Personal GetPersonal()
        {
            try
            {
                Open();

                return GetPersonalInfo();
            }
            catch (Exception e)
            {
                throw e;
            }
            finally
            {
                Close();
            }
        }

        public PersonalPhoto GetPersonalPhoto()
        {
            MemoryStream stream = new MemoryStream();

            try
            {
                Open();

                Personal personal = GetPersonalInfo();
                PersonalPhoto personalPhoto = new PersonalPhoto(personal);

                // use apdu.PhotoCommand, but allow custom commands via ExtendedCommands if set
                byte[][] photoCommand = apdu.PhotoCommand;
                byte[] responseBuffer;

                for (int i = 0; i < photoCommand.Length; i++)
                {
                    responseBuffer = Transmit(photoCommand[i]);
                    if (responseBuffer != null && responseBuffer.Length > 0)
                    {
                        stream.Write(responseBuffer, 0, responseBuffer.Length);
                    }
                }

                stream.Seek(0, SeekOrigin.Begin);

                personalPhoto.Photo = string.Format($"data:image/jpeg;base64,{Convert.ToBase64String(stream.ToArray())}");

                return personalPhoto;
            }
            catch (Exception e)
            {
                throw e;
            }
            finally
            {
                stream.Dispose();

                Close();
            }
        }

        private Personal GetPersonalInfo()
        {
            try
            {
                Personal personal = new Personal();

                personal.CitizenID = GetUTF8FromAsciiBytes(Transmit(apdu.CitizenIDCommand));

                string personalInfo = GetUTF8FromAsciiBytes(Transmit(apdu.PersonalInfoCommand));
                string thaiPersonalInfo = personalInfo.Substring(0, 100);
                string englishPersonalInfo = personalInfo.Substring(100, 100);

                personal.ThaiPersonalInfo = new PersonalInfo(thaiPersonalInfo);
                personal.EnglishPersonalInfo = new PersonalInfo(englishPersonalInfo);

                string dateOfBirth = personalInfo.Substring(200, 8);
                personal.DateOfBirth = new DateTime(Convert.ToInt32(dateOfBirth.Substring(0, 4)) - 543
                    , Convert.ToInt32(dateOfBirth.Substring(4, 2))
                    , Convert.ToInt32(dateOfBirth.Substring(6, 2))
                );

                personal.Sex = personalInfo.Substring(208, 1);

                string addressInfo = GetUTF8FromAsciiBytes(Transmit(apdu.AddressInfoCommand));

                personal.AddressInfo = new AddressInfo(addressInfo);

                string cardIssueExpire = GetUTF8FromAsciiBytes(Transmit(apdu.CardIssueExpireCommand));

                personal.IssueDate = new DateTime(Convert.ToInt32(cardIssueExpire.Substring(0, 4)) - 543
                    , Convert.ToInt32(cardIssueExpire.Substring(4, 2))
                    , Convert.ToInt32(cardIssueExpire.Substring(6, 2))
                );
                personal.ExpireDate = new DateTime(Convert.ToInt32(cardIssueExpire.Substring(8, 4)) - 543
                    , Convert.ToInt32(cardIssueExpire.Substring(12, 2))
                    , Convert.ToInt32(cardIssueExpire.Substring(14, 2))
                );
                personal.Issuer = GetUTF8FromAsciiBytes(Transmit(apdu.CardIssuerCommand));

                //personal.LaserID = GetUTF8FromAsciiBytes(Transmit(apdu.LaserIDCommand));

                return personal;
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        private string GetUTF8FromAsciiBytes(byte[] asciiBytes)
        {
            // protect against null
            if (asciiBytes == null || asciiBytes.Length == 0) return string.Empty;

            byte[] utf8 = Encoding.Convert(
                Encoding.GetEncoding("TIS-620"),
                Encoding.UTF8,
                asciiBytes
                );

            return Encoding.UTF8.GetString(utf8);
        }

        /// <summary>
        /// Public API: transmit any APDU and get response data (without SW1/SW2).
        /// This method will handle common responses like 0x61 (GET RESPONSE) and 0x6C (wrong length).
        /// </summary>
        public byte[] Transmit(byte[] command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            EnsureConnected();

            return SendCommand(command);
        }

        private byte[] SendCommand(byte[] command)
        {
            // allocate a sufficiently large buffer (cards may return up to several KB via chaining/GET RESPONSE)
            byte[] fullResponse = new byte[0];

            // first transmit the provided command
            byte[] responseBuffer = new byte[258];
            error = reader.Transmit(intPtr, command, ref responseBuffer);
            HandleError();

            ResponseApdu rapdu = new ResponseApdu(responseBuffer, IsoCase.Case2Short, reader.ActiveProtocol);

            // If SW1 = 0x6C => wrong length, SW2 = correct Le. Resend with correct Le
            if (rapdu.SW1 == 0x6C)
            {
                int correctLe = rapdu.SW2;
                // Build a new command same CLA/INS/P1/P2 but with Le = SW2
                byte cla = responseBuffer[0];
                byte ins = responseBuffer[1];
                byte p1 = responseBuffer[2];
                byte p2 = responseBuffer[3];

                byte[] newCmd = apdu.BuildCommand(cla, ins, p1, p2, null, correctLe);
                return SendCommand(newCmd);
            }

            // If SW1 = 0x61 => card has response data waiting. Use GET RESPONSE with SW2 as Le.
            if (rapdu.SW1 == 0x61)
            {
                int le = rapdu.SW2 == 0 ? 256 : rapdu.SW2; // SW2==0 often means 256 bytes
                byte[] getResp = apdu.GetResponseCommand(le);

                // transmit GET RESPONSE and fetch its data; append to fullResponse and check if more 61xx come back
                byte[] respPart = SendCommand(getResp); // recursion handles nested 61/6C
                if (respPart != null && respPart.Length > 0)
                {
                    fullResponse = fullResponse.Concat(respPart).ToArray();
                }
                return fullResponse;
            }

            // Normal response: return data portion (without trailing SW1/SW2)
            byte[] data = rapdu.GetData();

            // In some libraries ResponseApdu.GetData() may be null if no data
            if (data == null) data = new byte[0];

            // return directly (no SW)
            return data;
        }

        private void Close()
        {
            try
            {
                reader.Disconnect(SCardReaderDisposition.Leave);
            }
            catch { }
            try
            {
                context.Release();
            }
            catch { }
        }

        private void Open()
        {
            try
            {
                Thread.Sleep(1500);

                string[] szReaders = context.GetReaders();
                if (szReaders == null || szReaders.Length <= 0)
                    throw new PCSCException(SCardError.NoReadersAvailable, "Could not find any Smartcard reader.");

                error = reader.Connect(szReaders[0], SCardShareMode.Shared, SCardProtocol.T0 | SCardProtocol.T1);
                HandleError();

                intPtr = new IntPtr();
                switch (reader.ActiveProtocol)
                {
                    case SCardProtocol.T0:
                        intPtr = SCardPCI.T0;
                        break;
                    case SCardProtocol.T1:
                        intPtr = SCardPCI.T1;
                        break;
                    case SCardProtocol.Raw:
                        intPtr = SCardPCI.Raw;
                        break;
                    default:
                        throw new PCSCException(SCardError.ProtocolMismatch, "Protocol not supported: " + reader.ActiveProtocol.ToString());
                }

                error = reader.Status(out string[] readerNames, out SCardState state, out SCardProtocol protocol, out byte[] atrs);
                HandleError();

                if (atrs == null || atrs.Length < 2)
                    throw new PCSCException(SCardError.InvalidAtr);

                // choose APDU type based on ATR if needed (existing logic preserved)
                if (atrs[0] == 0x3B && atrs[1] == 0x67)
                {
                    apdu = new ThaiNationalIDCardAPDUCommandType01();
                }

                // Try select default AID (MinistryOfInterior) but allow caller to specify otherwise
                if (!SelectApplet(apdu.MinistryOfInteriorAppletCommand))
                    throw new Exception("SmartCard not support (Can't select Ministry of Interior Applet).");
            }
            catch (PCSCException e)
            {
                throw e;
            }
        }

        /// <summary>
        /// Public SelectApplet that accepts any AID
        /// </summary>
        public bool SelectApplet(byte[] aid)
        {
            EnsureConnected();

            byte[] command = apdu.SelectByAid(aid);
            byte[] responseBuffer = new byte[258];

            error = reader.Transmit(intPtr, command, ref responseBuffer);
            HandleError();

            ResponseApdu responseApdu = new ResponseApdu(responseBuffer, IsoCase.Case2Short, reader.ActiveProtocol);

            return responseApdu.SW1.Equals((byte)SW1Code.NormalDataResponse) || responseApdu.SW1.Equals((byte)SW1Code.Normal);
        }

        /// <summary>
        /// Ensure reader is connected (connect if not). Used by public Transmit.
        /// </summary>
        private void EnsureConnected()
        {
            if (reader == null) throw new InvalidOperationException("Reader not initialized.");
            // If not connected, try connect using first reader
            // We check reader.ActiveProtocol to see if connected
            try
            {
                if (reader.ActiveProtocol == SCardProtocol.Unset)
                {
                    string[] szReaders = context.GetReaders();
                    if (szReaders == null || szReaders.Length <= 0)
                        throw new PCSCException(SCardError.NoReadersAvailable, "Could not find any Smartcard reader.");

                    error = reader.Connect(szReaders[0], SCardShareMode.Shared, SCardProtocol.T0 | SCardProtocol.T1);
                    HandleError();

                    switch (reader.ActiveProtocol)
                    {
                        case SCardProtocol.T0:
                            intPtr = SCardPCI.T0;
                            break;
                        case SCardProtocol.T1:
                            intPtr = SCardPCI.T1;
                            break;
                        case SCardProtocol.Raw:
                            intPtr = SCardPCI.Raw;
                            break;
                    }
                }
            }
            catch (PCSCException)
            {
                // if anything failed, rethrow to caller
                throw;
            }
        }

        private void HandleError()
        {
            if (error == SCardError.Success)
                return;

            throw new PCSCException(error, SCardHelper.StringifyError(error));
        }
    }
}
