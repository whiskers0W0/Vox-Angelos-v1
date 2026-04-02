// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Threading.Tasks;
using VoxAngelos.Data;

namespace VoxAngelos.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LockoutModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public LockoutModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public int MinutesRemaining { get; set; } = 5;
        public int SecondsRemaining { get; set; } = 0;

        public async Task OnGetAsync()
        {
            var email = TempData["LockedOutEmail"]?.ToString();

            if (!string.IsNullOrEmpty(email))
            {
                var user = await _userManager.FindByEmailAsync(email);
                if (user?.LockoutEnd != null)
                {
                    var remaining = user.LockoutEnd.Value - DateTimeOffset.UtcNow;
                    if (remaining.TotalSeconds > 0)
                    {
                        MinutesRemaining = (int)remaining.TotalMinutes;
                        SecondsRemaining = (int)remaining.TotalSeconds % 60;
                    }
                    else
                    {
                        MinutesRemaining = 0;
                        SecondsRemaining = 0;
                    }
                }
            }
        }
    }
}