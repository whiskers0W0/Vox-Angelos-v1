using Microsoft.Extensions.Caching.Memory;

namespace VoxAngelos.Services;

public sealed record RegistrationFaceTicket(string IdImageHash, byte[] ReferenceImage,
    decimal LivenessConfidence, decimal Similarity, DateTimeOffset ExpiresAt);

public sealed class RegistrationFaceTicketStore(IMemoryCache cache)
{
    private static string Key(string token) => $"registration-face:{token}";

    public string Create(RegistrationFaceTicket ticket)
    {
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        cache.Set(Key(token), ticket, ticket.ExpiresAt);
        return token;
    }

    public bool TryGet(string token, out RegistrationFaceTicket? ticket) =>
        cache.TryGetValue(Key(token), out ticket);

    public void Remove(string token) => cache.Remove(Key(token));
}
