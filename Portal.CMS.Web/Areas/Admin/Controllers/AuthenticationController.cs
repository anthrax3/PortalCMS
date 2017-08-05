﻿using Portal.CMS.Entities.Enumerators;
using Portal.CMS.Services.Authentication;
using Portal.CMS.Web.Architecture.Helpers;
using Portal.CMS.Web.Areas.Admin.ViewModels.Authentication;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace Portal.CMS.Web.Areas.Admin.Controllers
{
    public class AuthenticationController : Controller
    {
        #region Manifest Constants

        const string IMAGE_DIRECTORY = "/Areas/Admin/Content/Media/Avatars";
        const string USER_ACCOUNT = "UserAccount";
        const string USER_ROLES = "UserRoles";

        #endregion Manifest Constants

        #region Dependencies

        readonly ILoginService _loginService;
        readonly IRegistrationService _registrationService;
        readonly IUserService _userService;
        readonly IRoleService _roleService;
        readonly ITokenService _tokenService;

        public AuthenticationController(ILoginService loginService, IRegistrationService registrationService, IUserService userService, IRoleService roleService, ITokenService tokenService)
        {
            _loginService = loginService;
            _registrationService = registrationService;
            _userService = userService;
            _roleService = roleService;
            _tokenService = tokenService;
        }

        #endregion Dependencies

        [HttpGet]
        public ActionResult Index()
        {
            return RedirectToAction(nameof(Index), "Home", new { area = "" });
        }

        [HttpGet]
        [OutputCache(Duration = 86400)]
        public ActionResult Login()
        {
            return View("_Login", new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View("_Login", model);

            var userId = await _loginService.LoginAsync(model.EmailAddress, model.Password);

            if (!userId.HasValue)
            {
                ModelState.AddModelError("InvalidCredentials", "Invalid Account Credentials");

                return View("_Login", model);
            }

            Session.Add(USER_ACCOUNT, await _userService.GetUserAsync(userId.Value));
            Session.Add(USER_ROLES, await _roleService.GetAsync(userId));

            return Content("Refresh");
        }

        [HttpGet]
        public ActionResult Logout()
        {
            Session.Clear();

            return RedirectToAction(nameof(Index), "Home", new { area = "" });
        }

        [HttpGet]
        [OutputCache(Duration = 86400)]
        public ActionResult Register()
        {
            return View("_Register", new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View("_Register", model);

            var isAdministrator = false;

            var userId = await _registrationService.RegisterAsync(model.EmailAddress, model.Password, model.GivenName, model.FamilyName);

            switch (userId.Value)
            {
                case -1:
                    ModelState.AddModelError("EmailAddressUsed", "The Email Address you entered is already registered");
                    return View("_Register", model);

                default:
                    if (await _userService.GetUserCountAsync() == 1)
                    {
                        await _roleService.UpdateAsync(userId.Value, new List<string> { nameof(Admin), "Authenticated" });

                        isAdministrator = true;
                    }
                    else
                    {
                        await _roleService.UpdateAsync(userId.Value, new List<string> { "Authenticated" });
                    }

                    Session.Add(USER_ACCOUNT, await _userService.GetUserAsync(userId.Value));
                    Session.Add(USER_ROLES, await _roleService.GetAsync(userId));

                    if (isAdministrator)
                        return Content("Setup");

                    return Content("Refresh");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Forgot(LoginViewModel model)
        {
            var token = await _tokenService.AddAsync(model.EmailAddress, UserTokenType.ForgottenPassword);

            if (!string.IsNullOrWhiteSpace(token))
            {
                var websiteName = SettingHelper.Get("Website Name");

                var recoveryLink = $@"http://{System.Web.HttpContext.Current.Request.Url.Authority}{Url.Action(nameof(Reset), "Authentication", new { id = token })}";

                EmailHelper.Send(new List<string> { model.EmailAddress }, "Password Reset", $"<p>You submitted a request on {websiteName} for assistance in resetting your password. To change your password please click on the link below and complete the requested information.</p><a href=\"{recoveryLink}\">Recover Account</a>");
            }

            return Content("Refresh");
        }

        [HttpGet]
        public ActionResult Reset(string id)
        {
            return View(new ResetViewModel { Token = id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Reset(ResetViewModel model)
        {
            if (!string.IsNullOrWhiteSpace(model.Password) && !string.IsNullOrWhiteSpace(model.ConfirmPassword))
            {
                if (!model.Password.Equals(model.ConfirmPassword, StringComparison.Ordinal))
                    ModelState.AddModelError("Confirmation", "The passwords you entered do not match.");
            }

            if (!ModelState.IsValid)
                return View(model);

            var result = await _tokenService.RedeemPasswordTokenAsync(model.Token, model.EmailAddress, model.Password);

            if (!string.IsNullOrWhiteSpace(result))
            {
                ModelState.AddModelError("Execution", result);

                return View(model);
            }

            return RedirectToAction(nameof(Index), "Home", new { area = "" });
        }
    }
}