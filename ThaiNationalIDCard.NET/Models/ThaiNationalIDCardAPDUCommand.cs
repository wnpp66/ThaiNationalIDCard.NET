using System;
using System.Collections.Generic;
using System.Linq;
using ThaiNationalIDCard.NET.Interfaces;

namespace ThaiNationalIDCard.NET.Models
{
    public abstract class ThaiNationalIDCardAPDUCommand : IThaiNationalIDCardAPDUCommand
    {
        // Default AID (Ministry of Interior) kept for backward compatibility
        public virtual byte[] MinistryOfInteriorAppletCommand => new byte[] { 0xA0, 0X00, 0x00, 0x00, 0x54, 0x48, 0x00, 0x01 };

        // Legacy convenience commands (kept, but you can build dynamically as well)
        public virtual byte[] CitizenIDCommand => new byte[] { 0x80, 0xb0, 0x00, 0x04, 0x02, 0x00, 0x0d };
        public virtual byte[] PersonalInfoCommand => new byte[] { 0x80, 0xb0, 0x00, 0x11, 0x02, 0x00, 0xd1 }; // note: original had 0x00,0x11; kept as virtual
        public virtual byte[] AddressInfoCommand => new byte[] { 0x80, 0xb0, 0x15, 0x79, 0x02, 0x00, 0x64 };
        public virtual byte[] CardIssueExpireCommand => new byte[] { 0x80, 0xb0, 0x01, 0x67, 0x02, 0x00, 0x12 };
        public virtual byte[] CardIssuerCommand => new byte[] { 0x80, 0xb0, 0x00, 0xf6, 0x02, 0x00, 0x64 };
        public virtual byte[] LaserIDCommand => new byte[] { 0x80, 0xb0, 0x00, 0x1D, 0x02, 0x00, 0x0D };

        public virtual byte[][] PhotoCommand => new byte[][]
        {
            new byte[]{ 0x80, 0xB0, 0x01, 0x7B, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x02, 0x7A, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x03, 0x79, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x04, 0x78, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x05, 0x77, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x06, 0x76, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x07, 0x75, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x08, 0x74, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x09, 0x73, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x0A, 0x72, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x0B, 0x71, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x0C, 0x70, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x0D, 0x6F, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x0E, 0x6E, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x0F, 0x6D, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x10, 0x6C, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x11, 0x6B, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x12, 0x6A, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x13, 0x69, 0x02, 0x00, 0xFF },
            new byte[]{ 0x80, 0xB0, 0x14, 0x68, 0x02, 0x00, 0xFF },
        };

        // A place to store user-registered or extended commands by name
        public Dictionary<string, byte[]> ExtendedCommands { get; } = new Dictionary<string, byte[]>();

        // --- Generic APDU helpers ---

        /// <summary>
        /// Build a plain APDU with optional data. If data is null or empty, LC is omitted in case of short APDU.
        /// This builds a short-form APDU: CLA INS P1 P2 [Lc] [Data] [Le?] - caller decides Le usage.
        /// </summary>
        public byte[] BuildCommand(byte cla, byte ins, byte p1, byte p2, byte[] data = null, int? le = null)
        {
            var header = new List<byte> { cla, ins, p1, p2 };

            if (data != null && data.Length > 0)
            {
                header.Add((byte)data.Length);
                header.AddRange(data);
            }

            if (le.HasValue)
            {
                header.Add((byte)le.Value);
            }

            return header.ToArray();
        }

        /// <summary>
        /// Convenience for GET RESPONSE (00 C0 00 00 Le)
        /// </summary>
        public byte[] GetResponseCommand(int le)
        {
            return new byte[] { 0x00, 0xC0, 0x00, 0x00, (byte)le };
        }

        /// <summary>
        /// Convenience for SELECT by AID (00 A4 04 00 Lc AID)
        /// </summary>
        public byte[] SelectByAid(byte[] aid)
        {
            return new byte[] { 0x00, 0xA4, 0x04, 0x00, (byte)aid.Length }.Concat(aid).ToArray();
        }

        /// <summary>
        /// Build a Read Binary style APDU for cards using custom CLA/INS (commonly 0x80 0xB0).
        /// offset: 2 bytes, length: 2 bytes
        /// This returns an APDU matching the original scheme used in this repo.
        /// </summary>
        public byte[] ReadBinary(int offset, int length, byte cla = 0x80, byte ins = 0xB0)
        {
            var p1 = (byte)((offset >> 8) & 0xFF);
            var p2 = (byte)(offset & 0xFF);

            // Some readers/cards expect a "extended" format for length; here we keep same style as original repo:
            return new byte[] { cla, ins, p1, p2, 0x02, (byte)((length >> 8) & 0xFF), (byte)(length & 0xFF) };
        }

        // Force child classes to provide GetResponse definition (keeps compatibility with existing derivatives)
        public abstract byte[] GetResponse();
        public abstract byte[] Select(byte[] command);

        public byte[] BuildCommand(byte cla, byte ins, byte p1, byte p2, object value, int correctLe)
        {
            throw new NotImplementedException();
        }
    }
}
