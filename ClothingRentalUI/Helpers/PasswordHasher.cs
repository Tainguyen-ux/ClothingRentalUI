using System;
using System.Security.Cryptography;

namespace ClothingRentalUI.Helpers;

public static class PasswordHasher
{
    private const int SaltSize = 16; // 128 bit
    private const int KeySize = 32; // 256 bit
    private const int Iterations = 10000;

    public static string HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize
        );
        var salt = Convert.ToBase64String(saltBytes);
        var key = Convert.ToBase64String(hashBytes);
        return $"{Iterations}.{salt}.{key}";
    }

    public static bool VerifyPassword(string password, string hashedPassword)
    {
        try
        {
            var parts = hashedPassword.Split('.', 3);
            if (parts.Length != 3) return false;

            var iterations = int.Parse(parts[0]);
            var saltBytes = Convert.FromBase64String(parts[1]);
            var key = parts[2];

            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                iterations,
                HashAlgorithmName.SHA256,
                KeySize
            );
            
            var keyBytes = Convert.FromBase64String(key);
            return CryptographicOperations.FixedTimeEquals(hashBytes, keyBytes);
        }
        catch
        {
            return false;
        }
    }
}
