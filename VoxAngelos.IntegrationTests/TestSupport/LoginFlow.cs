using System.Net;
using VoxAngelos.IntegrationTests.TestSupport;

namespace VoxAngelos.IntegrationTests.TestSupport;

public static class LoginFlow
{
    public sealed record LoginResult(HttpClient Client, HttpResponseMessage FinalResponse, string? RedirectLocation);

    /// <summary>
    /// Drives the full shared login -> email OTP -> role redirect chain exactly as a
    /// browser would (reCAPTCHA is skipped only because the app was launched with
    /// Testing:BypassRecaptcha=true). Returns an authenticated client plus the final
    /// (not-followed) redirect response so callers can assert the role-specific target.
    /// </summary>
    public static async Task<LoginResult> LoginAsync(
        IdentityTestServices identity,
        string email,
        string password)
    {
        var client = HttpClientExtensions.CreateAppClient(allowAutoRedirect: false);

        var loginResponse = await client.PostFormAsync(
            "/Identity/Account/Login",
            handler: null,
            fields: new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
                ["Input.RememberMe"] = "false"
            });

        if (loginResponse.StatusCode != HttpStatusCode.Redirect && loginResponse.StatusCode != HttpStatusCode.Found)
        {
            var body = await loginResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Login POST for {email} did not redirect to the OTP step (status {loginResponse.StatusCode}). Body: {body[..Math.Min(500, body.Length)]}");
        }

        var otpPageUrl = loginResponse.Headers.Location!.OriginalString;

        // Landing on the 2FA page is what makes the app generate/"send" the OTP.
        await client.GetAsync(otpPageUrl);

        var user = await identity.GetUserByEmailAsync(email);
        var otp = await identity.GenerateEmailOtpAsync(user.Id);

        var otpToken = await client.GetAntiforgeryTokenAsync(otpPageUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, otpPageUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.TwoFactorCode"] = otp,
                ["Input.RememberMachine"] = "false",
                ["__RequestVerificationToken"] = otpToken
            })
        };
        var finalResponse = await client.SendAsync(request);

        return new LoginResult(client, finalResponse, finalResponse.Headers.Location?.OriginalString);
    }
}
