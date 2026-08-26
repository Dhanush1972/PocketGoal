using System.Security.Cryptography;

namespace PocketGoal.Services
{
    public interface IPasswordHasherService
    {
        string HashPassword(string password);
        bool VerifyPassword(string password, string? hashedPassword);
    }

    public class PasswordHasherService : IPasswordHasherService
    {
        private const int SaltSize = 16; // 128 bit
        private const int KeySize = 32;   // 256 bit
        private const int Iterations = 100000;
        private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;
        private const char SegmentDelimiter = ':';

        public string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password cannot be empty", nameof(password));
            }

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithm,
                KeySize);

            return string.Join(
                SegmentDelimiter,
                Convert.ToHexString(hash),
                Convert.ToHexString(salt),
                Iterations,
                HashAlgorithm);
        }

        public bool VerifyPassword(string password, string? hashedPassword)
        {
            if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(password))
            {
                return false;
            }

            var segments = hashedPassword.Split(SegmentDelimiter);
            if (segments.Length != 4)
            {
                return false;
            }

            try
            {
                var hash = Convert.FromHexString(segments[0]);
                var salt = Convert.FromHexString(segments[1]);
                var iterations = int.Parse(segments[2]);
                var algorithm = new HashAlgorithmName(segments[3]);

                var inputHash = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    iterations,
                    algorithm,
                    hash.Length);

                return CryptographicOperations.FixedTimeEquals(inputHash, hash);
            }
            catch
            {
                return false;
            }
        }
    }
}
