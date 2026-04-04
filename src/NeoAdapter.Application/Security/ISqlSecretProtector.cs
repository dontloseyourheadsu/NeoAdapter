namespace NeoAdapter.Application.Security;

public interface ISqlSecretProtector
{
    string Protect(string plainTextPassword);

    string Unprotect(string storedPayload);
}