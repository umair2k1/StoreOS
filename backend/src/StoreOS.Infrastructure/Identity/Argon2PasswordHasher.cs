using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using StoreOS.Application.Identity;

namespace StoreOS.Infrastructure.Identity;

public class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int DegreeOfParallelism = 4;
    private const int Iterations = 3;
    private const int MemorySizeKb = 65536; // 64 MB

    public string Hash(string plainTextPassword)
    {
        if (string.IsNullOrEmpty(plainTextPassword))
            throw new ArgumentException("Password cannot be empty.", nameof(plainTextPassword));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = ComputeHash(plainTextPassword, salt);
        // Stored format: argon2id$<iterations>$<memoryKb>$<parallelism>$<base64salt>$<base64hash>
        return $"argon2id${Iterations}${MemorySizeKb}${DegreeOfParallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string plainTextPassword, string storedHash)
    {
        if (string.IsNullOrEmpty(plainTextPassword) || string.IsNullOrEmpty(storedHash))
            return false;

        try
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 6 || parts[0] != "argon2id") return false;
            var iterations = int.Parse(parts[1]);
            var memoryKb = int.Parse(parts[2]);
            var parallelism = int.Parse(parts[3]);
            var salt = Convert.FromBase64String(parts[4]);
            var expectedHash = Convert.FromBase64String(parts[5]);
            var actualHash = ComputeHash(plainTextPassword, salt, iterations, memoryKb, parallelism);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ComputeHash(string password, byte[] salt,
        int iterations = Iterations, int memoryKb = MemorySizeKb, int parallelism = DegreeOfParallelism)
    {
        using var argon2 = new Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            Iterations = iterations,
            MemorySize = memoryKb
        };
        return argon2.GetBytes(HashSize);
    }
}
