using System;
using System.Text;
using ThaiNationalIDCard.NET.Models;

namespace ThaiNationalIDCard.NET.Example.ConsoleApp
{
    class Program
    {

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding tis620 = Encoding.GetEncoding("TIS-620");

            try
            {
                ThaiNationalIDCardReader cardReader = new ThaiNationalIDCardReader();
                PersonalPhoto personalPhoto = cardReader.GetPersonalPhoto();

                Console.WriteLine($"CitizenID: {personalPhoto.CitizenID}");
                Console.WriteLine($"ThaiPersonalInfo: {personalPhoto.ThaiPersonalInfo}");
                Console.WriteLine($"EnglishPersonalInfo: {personalPhoto.EnglishPersonalInfo}");
                Console.WriteLine($"DateOfBirth: {personalPhoto.DateOfBirth}");
                Console.WriteLine($"Sex: {personalPhoto.Sex}");
                Console.WriteLine($"AddressInfo: {personalPhoto.AddressInfo}");
                Console.WriteLine($"IssueDate: {personalPhoto.IssueDate}");
                Console.WriteLine($"ExpireDate: {personalPhoto.ExpireDate}");
                Console.WriteLine($"Issuer: {personalPhoto.Issuer}");
                Console.WriteLine($"Photo: {personalPhoto.Photo}");
                Console.WriteLine($"LeserID: {personalPhoto.LaserID}");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            Console.WriteLine("Please any key to exit...");
            Console.ReadKey(true);
        }
    }
}
