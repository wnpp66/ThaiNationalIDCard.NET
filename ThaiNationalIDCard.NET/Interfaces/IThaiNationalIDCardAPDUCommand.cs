namespace ThaiNationalIDCard.NET.Interfaces
{
    public interface IThaiNationalIDCardAPDUCommand
    {
        byte[] GetResponse();
        byte[] Select(byte[] command);
        byte[] BuildCommand(byte cla, byte ins, byte p1, byte p2, object value, int correctLe);
        byte[] GetResponseCommand(int le);
        byte[] SelectByAid(byte[] aid);

        byte[] MinistryOfInteriorAppletCommand { get; }
        byte[] CitizenIDCommand { get; }
        byte[] PersonalInfoCommand { get; }
        byte[] AddressInfoCommand { get; }
        byte[] CardIssueExpireCommand { get; }
        byte[] CardIssuerCommand { get; }
        byte[] LaserIDCommand { get; }
        byte[][] PhotoCommand { get; }
    }
}
