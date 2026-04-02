using Microsoft.AspNetCore.Identity;
using Microsoft.CodeAnalysis.Scripting;

namespace VoxAngelos.Data
{
    public class BCryptPasswordHasher<TUser> : IPasswordHasher<TUser> where TUser : class
    {
        public string HashPassword(TUser user, string password)
        {
            // Work factor 12 is recommended for current hardware
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        }

        public PasswordVerificationResult VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
        {
            if (BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword))
            {
                return PasswordVerificationResult.Success;
            }
            return PasswordVerificationResult.Failed;
        }
    }
}