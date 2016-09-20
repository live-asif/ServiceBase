﻿using Host.Config;
using Host.Crypto;
using Host.Extensions;
using Host.Models;
using Host.Notification.Email;
using Host.Services;
using IdentityServer4.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Host.UI.Login
{
    public class RecoverController : Controller
    {
        private readonly ApplicationOptions _applicationOptions;
        private readonly ILogger<ExternalController> _logger;
        private readonly IUserAccountStore _userAccountStore;
        private readonly IIdentityServerInteractionService _interaction;
        private readonly IEmailSender _emailSender;
        private readonly ICrypto _crypto;
        private readonly IEmailFormatter _emailFormatter;

        public RecoverController(
            IOptions<ApplicationOptions> applicationOptions,
            ILogger<ExternalController> logger,
            IUserAccountStore userAccountStore,
            IIdentityServerInteractionService interaction,
            IEmailSender emailSender,
            ICrypto crypto,
            IEmailFormatter emailFormatter)
        {
            _applicationOptions = applicationOptions.Value;
            _logger = logger;
            _userAccountStore = userAccountStore;
            _interaction = interaction;
            _emailSender = emailSender;
            _crypto = crypto;
            _emailFormatter = emailFormatter;
        }

        [HttpGet("recover", Name = "Recover")]
        public async Task<IActionResult> Index(string returnUrl)
        {
            var vm = new RecoverViewModel();

            if (!String.IsNullOrWhiteSpace(returnUrl))
            {
                var request = await _interaction.GetAuthorizationContextAsync(returnUrl);
                if (request != null)
                {
                    vm.Email = request.LoginHint;
                    vm.ReturnUrl = returnUrl;
                }
            }

            return View(vm);
        }

        [HttpPost("recover")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(RecoverInputModel model)
        {
            if (ModelState.IsValid)
            {
                // Load user by email 
                var email = model.Email.ToLower();

                // Check if user with same email exists
                var userAccount = await _userAccountStore.LoadByEmailAsync(email);

                if (userAccount != null)
                {
                    userAccount.VerificationKey = StripUglyBase64(_crypto.Hash(_crypto.GenerateSalt()));
                    userAccount.VerificationPurpose = (int)VerificationKeyPurpose.ConfirmAccount;
                    userAccount.VerificationKeySentAt = DateTime.UtcNow;
                    // account.VerificationStorage = WebUtility.HtmlDecode(model.ReturnUrl);
                    userAccount.VerificationStorage = model.ReturnUrl;

                    var dictionary = new Dictionary<string, object>
                    {
                        { "Email", userAccount.Email },
                        { "Token", userAccount.VerificationKey },
                    };
                    var mailMessage = await _emailFormatter.FormatAsync("AccountRecoverEvent", userAccount, dictionary);
                    await _emailSender.SendEmailAsync(mailMessage);

                    // Redirect to success page by preserving the email provider name 
                    return Redirect(Url.Action("Success", "Recover", new
                    {
                        returnUrl = model.ReturnUrl,
                        provider = userAccount.Email.Split('@').LastOrDefault()
                    }));
                }
                else
                {
                    ModelState.AddModelError("", "User is deactivated.");
                }
            }

            var vm = new RecoverViewModel(model);
            return View(vm);
        }

        [HttpGet("recover/success", Name = "Success")]
        public async Task<IActionResult> Success(string returnUrl, string provider)
        {
            // select propper mail provider and render it as button 

            return View();
        }

        [HttpGet("recover/confirm/{key}", Name = "Confirm")]
        public async Task<IActionResult> Confirm(string key)
        {
            // Load token data from database 
            var userAccount = await _userAccountStore.LoadByVerificationKeyAsync(key);

            if (userAccount == null)
            {
                // ERROR
            }

            if (userAccount.VerificationPurpose != (int)VerificationKeyPurpose.ResetPassword)
            {
                // ERROR
            }

            var vm = new RecoverViewModel();
            return View(vm);
        }

        [HttpGet("recover/cancel/{key}", Name = "Cancel")]
        public async Task<IActionResult> Cancel(string key)
        {
            // Load token data from database 
            var userAccount = await _userAccountStore.LoadByVerificationKeyAsync(key);

            if (userAccount == null)
            {
                // ERROR
            }

            if (userAccount.VerificationPurpose != (int)VerificationKeyPurpose.ResetPassword)
            {
                // ERROR
            }

            if (userAccount.LastLoginAt != null)
            {
                // ERROR
            }

              

            return Redirect("~/");
        }

        static readonly string[] UglyBase64 = { "+", "/", "=" };
        protected virtual string StripUglyBase64(string s)
        {
            if (s == null) return s;
            foreach (var ugly in UglyBase64)
            {
                s = s.Replace(ugly, String.Empty);
            }
            return s;
        }


    }
}
