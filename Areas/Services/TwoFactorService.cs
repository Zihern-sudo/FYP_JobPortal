using OtpNet;
using System;

namespace JobPortal.Services
{
    public class TwoFactorService
    {
        public static string GenerateSecret()
        {
            var bytes = KeyGeneration.GenerateRandomKey(20);
            return Base32Encoding.ToString(bytes);
        }

        public static string GenerateQrCodeUrl(string email, string secret)
        {
            var issuer = "JobPortal";
            return $"otpauth://totp/{issuer}:{email}?secret={secret}&issuer={issuer}";
        }

        public static bool VerifyCode(string secret, string code)
        {
            var totp = new Totp(Base32Encoding.ToBytes(secret));
            return totp.VerifyTotp(code, out _, VerificationWindow.RfcSpecifiedNetworkDelay);
        }
    }
}
