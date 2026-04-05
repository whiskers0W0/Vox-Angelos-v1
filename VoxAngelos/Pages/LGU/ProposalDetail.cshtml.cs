using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VoxAngelos.Pages.LGU
{
    [Authorize(Policy = "RequireLGURole")]
    public class ProposalDetailModel : PageModel
    {
        public ProposalViewModel Proposal { get; set; } = new();

        public IActionResult OnGet(int id)
        {
            // ── Dummy data — replace with database lookup later ──
            // e.g. Proposal = _context.Proposals.FirstOrDefault(p => p.Id == id);

            var proposals = new List<ProposalViewModel>
            {
                new ProposalViewModel
                {
                    Id = 1,
                    Title = "Road and Utility Maintenance Improvement",
                    Category = "Road, Utilities",
                    Office = "Engineering Office",
                    SubmissionDate = "September 22, 2025",
                    AuthorName = "Anne Curtis",
                    AuthorInitials = "AC",
                    AuthorEmail = "example@gmail.com",
                    ContactNumber = "0123-456-7891",
                    Upvotes = 100,
                    Downvotes = 12,
                    Description = "This project recommendation addresses a minor but growing infrastructure issue observed in the community that requires immediate attention. Small damages in public roads and nearby utility systems have been noted, which, if left unattended, may lead to safety risks and higher repair costs in the future. These concerns affect daily transportation and overall public safety within the area.\n\nThe recommended action is the early repair of damaged road surfaces, proper securing of utility cables, and the inspection of nearby drainage or sewer components. Implementing these repairs promptly will help prevent further deterioration, reduce accidents, and ensure the continued functionality of essential public infrastructure.\n\nThis project aims to improve safety, maintain accessibility, and support the long-term durability of community facilities. Residents, pedestrians, motorists, and nearby establishments are expected to benefit from these improvements. Given the potential impact on daily activities, this project is considered a medium priority and is expected to create a safer and more reliable environment once completed.",
                    Attachments = new List<string> { "", "", "", "" }
                },
                new ProposalViewModel
                {
                    Id = 2,
                    Title = "Road Network Improvement in Barangay Pulung Bulu",
                    Category = "Infrastructure",
                    Office = "Engineering Office",
                    SubmissionDate = "September 22, 2025",
                    AuthorName = "Marian Rivera",
                    AuthorInitials = "MR",
                    AuthorEmail = "mrivera@example.com",
                    ContactNumber = "0987-654-3210",
                    Upvotes = 85,
                    Downvotes = 20,
                    Description = "This recommendation focuses on addressing the deteriorating road conditions in Barangay Pulung Bulu which have been causing accidents and hampering the movement of goods and residents. The proposed project involves repaving key road sections, installing proper drainage to prevent flooding, and adding road safety signage throughout the area.",
                    Attachments = new List<string> { "" }
                },
                new ProposalViewModel
                {
                    Id = 3,
                    Title = "Mobile Health Clinic Expansion",
                    Category = "Health",
                    Office = "City Health Office",
                    SubmissionDate = "September 22, 2025",
                    AuthorName = "Jose Dalisay",
                    AuthorInitials = "JD",
                    AuthorEmail = "jdalisay@example.com",
                    ContactNumber = "0912-345-6789",
                    Upvotes = 72,
                    Downvotes = 8,
                    Description = "A short description that does not need a View More button.",
                    Attachments = new List<string>()
                }
            };

            var proposal = proposals.FirstOrDefault(p => p.Id == id);

            if (proposal == null)
                return RedirectToPage("/LGU/Leaderboard");

            Proposal = proposal;
            return Page();
        }
    }

    public class ProposalViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string Office { get; set; } = "";
        public string SubmissionDate { get; set; } = "";
        public string AuthorName { get; set; } = "";
        public string AuthorInitials { get; set; } = "";
        public string AuthorEmail { get; set; } = "";
        public string ContactNumber { get; set; } = "";
        public int Upvotes { get; set; }
        public int Downvotes { get; set; }
        public string Description { get; set; } = "";
        public List<string> Attachments { get; set; } = new();
    }
}
