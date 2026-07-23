namespace VoxAngelos.Services
{
    // Central definition of where private identity-verification media (ID photos, live
    // selfies) lives on disk. App_Data sits outside wwwroot, so ASP.NET Core's static
    // file middleware (which only ever serves wwwroot) can never expose it directly —
    // the only way to read these files is through ReviewApplicationModel's
    // Admin-authorized OnGetIdentityMediaAsync handler.
    public static class IdentityDocumentStorage
    {
        public static string IdsFolder(IWebHostEnvironment env) =>
            Path.Combine(env.ContentRootPath, "App_Data", "identity-documents", "ids");

        public static string SelfiesFolder(IWebHostEnvironment env) =>
            Path.Combine(env.ContentRootPath, "App_Data", "identity-documents", "selfies");
    }
}
