namespace StoreOS.Application.Identity;

public interface IPasswordHasher
{
    string Hash(string plainTextPassword);
    bool Verify(string plainTextPassword, string storedHash);
}
