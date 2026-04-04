using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.DataProtection;

namespace NeoAdapter.Application.Security;

public sealed class SqlSecretProtector(IDataProtectionProvider dataProtectionProvider) : ISqlSecretProtector
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("NeoAdapter.SqlPassword");

    public string Protect(string plainTextPassword)
    {
        if (string.IsNullOrWhiteSpace(plainTextPassword))
        {
            throw new InvalidOperationException("SQL password is required.");
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = KeyDerivation.Pbkdf2(
            password: plainTextPassword,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 100_000,
            numBytesRequested: 32);

        var cipher = _protector.Protect(plainTextPassword);
        return $"v1:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}:{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cipher))}";
    }

    public string Unprotect(string storedPayload)
    {
        if (string.IsNullOrWhiteSpace(storedPayload))
        {
            throw new InvalidOperationException("Stored SQL password payload is missing.");
        }

        var parts = storedPayload.Split(':');
        if (parts.Length != 4 || parts[0] != "v1")
        {
            throw new InvalidOperationException("Stored SQL password payload is invalid.");
        }

        var protectedPassword = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
        var password = _protector.Unprotect(protectedPassword);

        // Verify against stored salt/hash to guard against payload tampering.
        var salt = Convert.FromBase64String(parts[1]);
        var expectedHash = Convert.FromBase64String(parts[2]);
        var actualHash = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 100_000,
            numBytesRequested: 32);

        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
        {
            throw new InvalidOperationException("Stored SQL password hash verification failed.");
        }

        return password;
    }
}