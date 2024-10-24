﻿using Dating.Data;
using System.Net.Mail;
using Dating.Models;
using Dating.Email;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using SendGrid.Helpers.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Google.Apis.Util;

namespace Dating.Controllers
{
    public class UsersController : Controller
    {

        private readonly AppDbContext _dbContext;
        private readonly ILogger<UsersController> _logger;

        public UsersController(AppDbContext dbContext, ILogger<UsersController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }




        //Tạo người dùng mới


        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [Route("register")]
        public IActionResult Register(UsersModels userViewModel, [FromServices] IWebHostEnvironment webHostEnvironment)
        {
            if (ModelState.IsValid)
            {
                var existingEmail = _dbContext.Users.FirstOrDefault(u => u.email == userViewModel.email);
                if (existingEmail != null)
                {
                    ModelState.AddModelError("Email", "Email người dùng đã tồn tại, vui lòng chọn Email khác.");
                    return View(userViewModel);
                }

                var user = new UsersModels
                {
                    username = userViewModel.username,
                    sdt = userViewModel.sdt,
                    email = userViewModel.email,
                    password = userViewModel.password,
                    gender = userViewModel.gender,
                    date_of_birth = userViewModel.date_of_birth,
                    bio = userViewModel.bio,
                    location = userViewModel.location,
                    latitude = userViewModel.latitude,
                    longitude = userViewModel.longitude,
                    iUsersRoleID = 2,
                    created_at = DateTime.Now
                };

                if (userViewModel.ProfileImage != null && userViewModel.ProfileImage.Length > 0)
                {
                    string folderPath = Path.Combine(webHostEnvironment.WebRootPath, "uploads");
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                    }

                    string filePath = Path.Combine(folderPath, Path.GetFileName(userViewModel.ProfileImage.FileName));
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        userViewModel.ProfileImage.CopyTo(stream);
                    }
                    user.profile_picture = "/uploads/" + Path.GetFileName(userViewModel.ProfileImage.FileName);
                }

                _dbContext.Users.Add(user);
                _dbContext.SaveChanges();
                TempData["SuccessMessage"] = "Đăng ký thành công! Vui lòng đăng nhập.";
                return RedirectToAction("Login");
            }
            else
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                foreach (var error in errors)
                {
                    ModelState.AddModelError(string.Empty, error);
                }
            }
            return View(userViewModel);
        }




        [HttpGet]
        [Route("login")]
        public IActionResult Login()
        {
            return View();
        }


        //Đăng nhập

        [HttpPost]
        [Route("login")]
        public async Task<IActionResult> Login(DangNhapViewModel model)
        {
            try
            {
                var user = await _dbContext.Users
                    .SingleOrDefaultAsync(x => x.email == model.Email && x.password == model.Password);
                var admin = await _dbContext.Admins
                    .SingleOrDefaultAsync(x => x.email == model.Email && x.password == model.Password);

                if (user != null)
                {
                    HttpContext.Session.SetString("UserName", user.username.ToString());
                    HttpContext.Session.SetInt32("Role", user.iUsersRoleID);

                    TempData["Role"] = user.iUsersRoleID;

                    // Kiểm tra hồ sơ của người dùng
                    var userProfile = await _dbContext.Profiles
                        .SingleOrDefaultAsync(up => up.user_profile_id == user.user_id);

                    // Lưu giá trị isApproved vào session
                    if (userProfile != null)
                    {
                        HttpContext.Session.SetString("IsProfileApproved", userProfile.isApproved ? "True" : "False");
                    }
                    else
                    {
                        HttpContext.Session.SetString("IsProfileApproved", "False");
                    }


                    var verificationCode = new Random().Next(100000, 999999).ToString();

                    var emailService = HttpContext.RequestServices.GetService<EmailService>();
                    if (emailService != null)
                    {
                        await emailService.SendEmailAsync(user.email, "Mã xác nhận đăng nhập", $"Mã xác nhận của bạn là: {verificationCode}");
                    }
                    else
                    {
                        ViewBag.ErrorMessage = "Không thể gửi email xác nhận. Vui lòng thử lại.";
                        return View();
                    }

                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.user_id.ToString()),
                        new Claim(ClaimTypes.Email, user.email), 
                        new Claim(ClaimTypes.Name, user.username.ToString())
                    };

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                    HttpContext.Session.SetString("VerificationCode", verificationCode);

                    return RedirectToAction("UserList", "Users");
                }
                else if (admin != null)
                {
                    HttpContext.Session.SetString("UserName", admin.iAdmin.ToString());
                    HttpContext.Session.SetInt32("Role", 1);

                    TempData["Role"] = 1;

                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, admin.iAdmin.ToString()),
                        new Claim(ClaimTypes.Email, admin.email),
                        new Claim(ClaimTypes.Name, admin.sAdminName),
                        new Claim(ClaimTypes.Role, "Admin")
                    };

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                    return RedirectToAction("Index", "Users");
                }
                else
                {
                    ViewBag.ErrorMessage = "Tài khoản hoặc mật khẩu sai";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Đã xảy ra lỗi trong quá trình đăng nhập: {ex.Message}");
                ViewBag.ErrorMessage = "Đã xảy ra lỗi, vui lòng thử lại.";
            }

            return View();
        }

        //Mã xác nhận

        [HttpGet]
        [Route("verifycode")]
        public IActionResult VerifyCode()
        {
            return View();
        }

        [HttpPost]
        [Route("verifycode")]
        public IActionResult VerifyCode(string enteredCode)
        {
            var sessionCode = HttpContext.Session.GetString("VerificationCode");

            if (sessionCode == enteredCode)
            {
                ViewBag.SuccessMessage = "Xác nhận mã thành công! Bạn sẽ được chuyển hướng trong giây lát.";

                HttpContext.Session.Remove("VerificationCode");
            }
            else
            {
                ViewBag.ErrorMessage = "Mã xác nhận không chính xác";
            }

            return View();
        }


        //Đăng xuất

        [HttpPost]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();

            HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).GetAwaiter().GetResult();

            HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

            return RedirectToAction("Login", "Users");
        }


        //Quên mật khẩu

        [HttpGet]
        [Route("forgotpassword")]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [Route("forgotpassword")]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            try
            {
                var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.email == email);

                if (user == null)
                {
                    ViewBag.ErrorMessage = "Email không tồn tại trong hệ thống.";
                    return View();
                }

                var newPassword = GenerateRandomPassword();

                user.password = newPassword;
                await _dbContext.SaveChangesAsync();

                var emailService = HttpContext.RequestServices.GetService<EmailService>();
                if (emailService != null)
                {
                    await emailService.SendEmailAsync(user.email, "Cấp lại mật khẩu", $"Mật khẩu mới của bạn là: {newPassword}");
                    TempData["Message"] = "Mật khẩu mới đã được gửi đến email của bạn. Hãy kiểm tra email và đăng nhập lại.";
                    return RedirectToAction("Login");
                }
                else
                {
                    ViewBag.ErrorMessage = "Không thể gửi email cấp lại mật khẩu. Vui lòng thử lại.";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Đã xảy ra lỗi trong quá trình cấp lại mật khẩu: {ex.Message}");
                ViewBag.ErrorMessage = "Đã xảy ra lỗi, vui lòng thử lại.";
            }

            return View();
        }


        //Random mật khẩu mới

        private string GenerateRandomPassword(int length = 8)
        {
            const string validChars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(validChars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }


        /*public async Task<IActionResult> UserList()
        {
            // Lấy email của người dùng hiện tại từ Claims
            var currentUserEmail = User.FindFirstValue(ClaimTypes.Email);

            // Tìm người dùng hiện tại dựa trên email
            var currentUser = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.email == currentUserEmail);

            // Lấy ID người dùng hiện tại
            var currentUserId = currentUser?.user_id; // Trường hợp không tìm thấy người dùng, currentUserId sẽ là null

            // Lấy danh sách người dùng mà không bao gồm người dùng hiện tại
            var users = await _dbContext.Users
                .Where(u => u.user_id != currentUserId) // Lọc ra người dùng không phải là người đang đăng nhập
                .ToListAsync();

            return View(users);
        }*/


        //Danh sách người dùng

        public async Task<IActionResult> UserList()
        {
            // Lấy email của người dùng hiện tại từ Claims
            var currentUserEmail = User.FindFirstValue(ClaimTypes.Email);

            // Tìm người dùng hiện tại dựa trên email
            var currentUser = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.email == currentUserEmail);

            // Lấy ID người dùng hiện tại
            var currentUserId = currentUser?.user_id; // Trường hợp không tìm thấy người dùng, currentUserId sẽ là null

            // Lấy danh sách người dùng mà không bao gồm người dùng hiện tại
            var users = await _dbContext.Users
                .Where(u => u.user_id != currentUserId) // Lọc ra người dùng không phải là người đang đăng nhập
                .ToListAsync();

            // Lấy danh sách ID của những người đã lướt qua hoặc thích
            var skippedUserIds = await _dbContext.Skips
                .Where(s => s.user_skip_id == currentUserId)
                .Select(s => s.skippe_user_id)
                .ToListAsync();

            var likedUserIds = await _dbContext.Likes
                .Where(l => l.userlike_id == currentUserId)
                .Select(l => l.liked_user_id)
                .ToListAsync();

            // Lọc ra những người đã lướt qua hoặc đã thích
            var filteredUsers = users
                .Where(u => !skippedUserIds.Contains(u.user_id) && !likedUserIds.Contains(u.user_id))
                .ToList();

            // Nếu không còn người dùng nào trong danh sách đã lọc, trả về người cuối cùng trong danh sách gốc
            if (!filteredUsers.Any() && users.Any())
            {
                filteredUsers.Add(users.Last()); // Thêm người cuối cùng vào danh sách
                ViewBag.NotificationMessage = "Bạn đã xem hết hồ sơ người dùng!";
            }


            return View(filteredUsers);
        }



        //Lưu người đã thích và match

        [HttpPost]
        public JsonResult SaveLike(int likedUserId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (currentUserId == null)
            {
                return Json(new { code = 400, mss = "Bạn phải đăng nhập" });
            }

            int userId = int.Parse(currentUserId);

            bool alreadyLiked = _dbContext.Likes.Any(l => l.userlike_id == userId && l.liked_user_id == likedUserId);

            if (!alreadyLiked)
            {
                var like = new LikeModels
                {
                    userlike_id = userId,
                    liked_user_id = likedUserId,
                    created_at = DateTime.Now
                };

                _dbContext.Likes.Add(like);
                _dbContext.SaveChanges();


                bool isMatch = _dbContext.Likes.Any(l => l.userlike_id == likedUserId && l.liked_user_id == userId);

                if (isMatch)
                {
                    var match = new MatchModels
                    {
                        user1_id = userId,
                        user2_id = likedUserId,
                        match_date = DateTime.Now
                    };
                    _dbContext.Matches.Add(match);
                    _dbContext.SaveChanges();

                    return Json(new { code = 200, mss = "Match!", alreadyLiked = false });
                }
            }


            return Json(new { alreadyLiked });
        }


        //Lưu người đã bỏ qua

        [HttpPost]
        public JsonResult SaveSkip(int likedUserId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (currentUserId == null)
            {
                return Json(new { code = 400, mss = "Bạn phải đăng nhập" });
            }

            int userId = int.Parse(currentUserId);

            bool alreadyLiked = _dbContext.Skips.Any(l => l.user_skip_id == userId && l.skippe_user_id == likedUserId);

            if (!alreadyLiked)
            {
                var skips = new SkipModels
                {
                    user_skip_id = userId,
                    skippe_user_id = likedUserId
                };

                _dbContext.Skips.Add(skips);
                _dbContext.SaveChanges();

            }


            return Json(new { alreadyLiked });
        }


        //Tạo hồ sơ

        [Authorize]
        public IActionResult CreateProfile()
        {
            return View();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> CreateProfile(UserProfile userProfile)
        {
            if (ModelState.IsValid)
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (userId != null)
                {
                    int parsedUserId = int.Parse(userId);

                    var existingProfile = await _dbContext.Profiles
                        .FirstOrDefaultAsync(up => up.user_profile_id == parsedUserId);

                    if (existingProfile != null)
                    {
                        HttpContext.Session.SetString("IsProfileApproved", existingProfile.isApproved.ToString());

                        if (existingProfile.isApproved)
                        {
                            TempData["Message"] = "Hồ sơ của bạn đã được duyệt trước đó.";
                            return RedirectToAction("UserList");
                        }
                        else
                        {
                            TempData["Message"] = "Hồ sơ của bạn đang chờ duyệt.";
                            return RedirectToAction("UserList");
                        }
                    }

                    userProfile.user_profile_id = parsedUserId;
                    userProfile.isApproved = false;

                    _dbContext.Add(userProfile);
                    await _dbContext.SaveChangesAsync();

                    HttpContext.Session.SetString("IsProfileApproved", userProfile.isApproved.ToString());
                    TempData["Message"] = "Hồ sơ của bạn đã được tạo thành công và đang chờ duyệt.";
                    return RedirectToAction("UserList");
                }

                ModelState.AddModelError("", "Không thể xác định người dùng hiện tại.");
            }

            return View(userProfile);
        }


        //Danh sách hồ sơ người dùng giành cho Admin

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            var profiles = await (from profile in _dbContext.Profiles
                                  join user in _dbContext.Users
                                  on profile.user_profile_id equals user.user_id
                                  where !profile.isApproved
                                  select new UserProfileViewModel
                                  {
                                      ProfileId = profile.profile_id,
                                      Occupation = profile.occupation,
                                      RelationshipStatus = profile.relationship_status,
                                      LookingFor = profile.looking_for,
                                      Hobbies = profile.hobbies,
                                      Height = profile.height,
                                      Weight = profile.weight,
                                      IsApproved = profile.isApproved,
                                      Username = user.username,
                                      Gender = user.gender,
                                      Bio = user.bio,
                                      ProfilePicture = user.profile_picture,
                                      Age = user.Age,
                                      Location = user.location
                                  }).ToListAsync();

            return View(profiles);
        }

        //Duyệt hồ sơ giành cho Admin

        public async Task<IActionResult> Approve(int? id)
        {
            var adminId = User.FindFirstValue("AdminId");

            if (id == null)
            {
                return NotFound();
            }

            var userProfile = await _dbContext.Profiles.FindAsync(id);
            if (userProfile == null)
            {
                return NotFound();
            }

            userProfile.isApproved = true;
            _dbContext.Update(userProfile);
            await _dbContext.SaveChangesAsync();

            return RedirectToAction("Index");
        }


        //Xem hồ sơ của mình

        [Authorize]
        public async Task<IActionResult> ViewProfile(int? id)
        {
            ViewBag.CurrentUserId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

            int parsedUserId;
            if (id == null)
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (userId == null)
                {
                    return RedirectToAction("Login", "Users");
                }

                parsedUserId = int.Parse(userId);
            }
            else
            {
                parsedUserId = id.Value;
            }

            var userProfile = await (from profile in _dbContext.Profiles
                                     join user in _dbContext.Users on profile.user_profile_id equals user.user_id
                                     where profile.user_profile_id == parsedUserId
                                     select new
                                     {
                                         ProfileId = profile.profile_id,
                                         Occupation = profile.occupation,
                                         RelationshipStatus = profile.relationship_status,
                                         LookingFor = profile.looking_for,
                                         Hobbies = profile.hobbies,
                                         Height = profile.height,
                                         Weight = profile.weight,
                                         IsApproved = profile.isApproved,
                                         Username = user.username,
                                         Gender = user.gender,
                                         Bio = user.bio,
                                         ProfilePicture = user.profile_picture,
                                         Age = user.Age,
                                         Location = user.location
                                     }).FirstOrDefaultAsync();

            if (userProfile == null)
            {
                return NotFound("Hồ sơ không tồn tại.");
            }

            var viewModel = new UserProfileViewModel
            {
                ProfileId = userProfile.ProfileId,
                Occupation = userProfile.Occupation,
                RelationshipStatus = userProfile.RelationshipStatus,
                LookingFor = userProfile.LookingFor,
                Hobbies = userProfile.Hobbies,
                Height = userProfile.Height,
                Weight = userProfile.Weight,
                IsApproved = userProfile.IsApproved,
                Username = userProfile.Username,
                Gender = userProfile.Gender,
                Bio = userProfile.Bio,
                ProfilePicture = userProfile.ProfilePicture,
                Age = userProfile.Age,
                Location = userProfile.Location
            };

            return View(viewModel);
        }

        //Sửa thông tin người dùng

        public IActionResult EditUser(int id)
        {
            var user = _dbContext.Users.Find(id);
            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }


        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditUser(int id, [Bind("user_id,username,sdt,gender,date_of_birth,bio,ProfileImage,location, password, email, iUsersRoleID, latitude, longitude")] UsersModels updatedUser)
        {
            if (id != updatedUser.user_id)
            {
                return NotFound();
            }
            
            if (ModelState.IsValid)
            {
                try
                {
                    var existingUser = _dbContext.Users.FirstOrDefault(u => u.user_id == id);
                    if (existingUser == null)
                    {
                        return NotFound();
                    }

                    existingUser.username = updatedUser.username;
                    existingUser.sdt = updatedUser.sdt;
                    existingUser.gender = updatedUser.gender;
                    existingUser.date_of_birth = updatedUser.date_of_birth;
                    existingUser.bio = updatedUser.bio;
                    existingUser.location = updatedUser.location;

                    if (updatedUser.ProfileImage != null)
                    {
                        var fileName = Path.GetFileName(updatedUser.ProfileImage.FileName);
                        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads", fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            updatedUser.ProfileImage.CopyTo(stream);
                        }

                        existingUser.profile_picture = $"/uploads/{fileName}";
                    }

                    _dbContext.Update(existingUser);
                    _dbContext.SaveChanges();
                    TempData["SuccessMessage"] = "Thông tin người dùng đã được cập nhật thành công.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_dbContext.Users.Any(u => u.user_id == updatedUser.user_id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(EditUser), new { id = updatedUser.user_id });
            }

            TempData["ErrorMessage"] = "Lỗi khi cập nhật thông tin người dùng.";
            return View(updatedUser);
        }


        //Sửa thông tin hồ sơ
        public IActionResult EditProfile(int id)
        {
            var profile = _dbContext.Profiles.FirstOrDefault(p => p.user_profile_id == id);
            if (profile == null)
            {
                return NotFound();
            }

            return View(profile);
        }

        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditProfile(int id, [Bind("profile_id,user_profile_id,occupation,relationship_status,gender_looking,looking_for,hobbies,height,weight,isApproved")] UserProfile updatedProfile)
        {
            var existingProfile = _dbContext.Profiles.FirstOrDefault(p => p.user_profile_id == id);

            if (existingProfile == null)
            {
                return NotFound();
            }

            if (existingProfile.profile_id != updatedProfile.profile_id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    existingProfile.occupation = updatedProfile.occupation;
                    existingProfile.relationship_status = updatedProfile.relationship_status;
                    existingProfile.looking_for = updatedProfile.looking_for;
                    existingProfile.gender_looking = updatedProfile.gender_looking;
                    existingProfile.hobbies = updatedProfile.hobbies;
                    existingProfile.height = updatedProfile.height;
                    existingProfile.weight = updatedProfile.weight;
                    existingProfile.isApproved = updatedProfile.isApproved;

                    _dbContext.Update(existingProfile);
                    _dbContext.SaveChanges();

                    TempData["SuccessMessage"] = "Thông tin hồ sơ đã được cập nhật thành công.";
                    return RedirectToAction(nameof(EditUser), new { id = id });
                }
                catch (DbUpdateConcurrencyException)
                {
                    return NotFound();
                }
            }

            TempData["ErrorMessage"] = "Lỗi khi cập nhật thông tin hồ sơ.";
            return View(updatedProfile);
        }


        //Xem hồ sơ cá nhân của người khác

        [HttpPost]
        public async Task<IActionResult> ProfilePerson(int likedUserId)
        {
            var userProfile = await (from profile in _dbContext.Profiles
                                     join user in _dbContext.Users on profile.user_profile_id equals user.user_id
                                     where user.user_id == likedUserId
                                     select new UserProfileViewModel
                                     {
                                         ProfileId = profile.profile_id,
                                         Occupation = profile.occupation,
                                         RelationshipStatus = profile.relationship_status,
                                         LookingFor = profile.looking_for,
                                         Hobbies = profile.hobbies,
                                         Height = profile.height,
                                         Weight = profile.weight,
                                         IsApproved = profile.isApproved,
                                         Username = user.username,
                                         Gender = user.gender,
                                         Bio = user.bio,
                                         ProfilePicture = user.profile_picture,
                                         Age = user.Age,
                                         Location = user.location
                                     }).FirstOrDefaultAsync();

            if (userProfile == null)
            {
                return NotFound("Hồ sơ không tồn tại.");
            }

            return View("ProfilePerson", userProfile);
        }


        [HttpGet]
        public async Task<IActionResult> ViewMatches()
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = int.Parse(currentUserId);

            var matches = await (from match in _dbContext.Matches
                                 where match.user1_id == userId || match.user2_id == userId
                                 join user in _dbContext.Users on (match.user1_id == userId ? match.user2_id : match.user1_id) equals user.user_id
                                 select new MatchViewModel
                                 {
                                     MatchId = match.match_id,
                                     Username = user.username,
                                     ProfilePicture = user.profile_picture,
                                     UserId = user.user_id
                                 }).ToListAsync();

            return View(matches);
        }



        // Hiển thị lịch sử chat
        public IActionResult Chat(int receiverId)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int senderId = int.Parse(userId); // Lấy ID người dùng hiện tại

            // Truy vấn các tin nhắn giữa người dùng hiện tại và người nhận
            var messages = _dbContext.Messages
                                   .Where(m => (m.sender_id == senderId && m.receiver_id == receiverId) ||
                                               (m.sender_id == receiverId && m.receiver_id == senderId))
                                   .OrderBy(m => m.sent_at)
                                   .ToList();
            var senderProfilePicture = _dbContext.Users
            .Where(u => u.user_id == receiverId)
            .Select(u => u.profile_picture) // Lấy ảnh đại diện của người gửi
            .FirstOrDefault();


            var chatViewModel = new ChatViewModel
            {
                Messages = messages,
                ReceiverId = receiverId,
                ReceiverUsername = _dbContext.Users.Where(u => u.user_id == receiverId)
                                                 .Select(u => u.username)
                                                 .FirstOrDefault(),
                SenderProfilePicture = senderProfilePicture
            };

            return View(chatViewModel);
        }

        // Gửi tin nhắn
        //[HttpPost]
        //public IActionResult SendMessage(int receiverId, string content)
        //{
        //    // Lấy ID người dùng hiện tại
        //    var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        //    // Kiểm tra xem userId có hợp lệ hay không
        //    if (int.TryParse(userId, out int senderId) && !string.IsNullOrEmpty(content))
        //    {
        //        // Tạo tin nhắn mới
        //        var newMessage = new MessagesSModels
        //        {
        //            sender_id = senderId,
        //            receiver_id = receiverId,
        //            content = content,
        //            sent_at = DateTime.Now
        //        };

        //        _dbContext.Messages.Add(newMessage);
        //        _dbContext.SaveChanges();
        //    }

        //    return RedirectToAction("Chat", new { receiverId = receiverId });
        //}

        [HttpPost]
        public JsonResult SendMessage(int receiverId, string content)
        {
            // Lấy ID người dùng hiện tại
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            // Kiểm tra xem userId có hợp lệ hay không
            if (int.TryParse(userId, out int senderId) && !string.IsNullOrEmpty(content))
            {
                // Tạo tin nhắn mới
                var newMessage = new MessagesSModels
                {
                    sender_id = senderId,
                    receiver_id = receiverId,
                    content = content,
                    sent_at = DateTime.Now
                };

                _dbContext.Messages.Add(newMessage);
                _dbContext.SaveChanges();

                // Trả về kết quả thành công với thông tin tin nhắn
                return Json(new
                {
                    success = true,
                    message = newMessage
                });
            }

            // Nếu không thành công, trả về thông báo lỗi
            return Json(new { success = false, message = "Có lỗi xảy ra khi gửi tin nhắn." });
        }

    }
}
