using Aiursoft.MarkToHtml.Authorization;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Models.UsersViewModels;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.MarkToHtml.Services.FileStorage;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;


namespace Aiursoft.MarkToHtml.Controllers;

/// <summary>
/// This controller is used to handle users related actions like create, edit, delete, etc.
/// </summary>
[Authorize]
[LimitPerMin]
public class UsersController(
    RoleManager<IdentityRole> roleManager,
    UserManager<User> userManager,
    TemplateDbContext context,
    StorageService storageService)
    : Controller
{
    [Authorize(Policy = AppPermissionNames.CanReadUsers)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Directory",
        CascadedLinksIcon = "users",
        CascadedLinksOrder = 9998,
        LinkText = "Users",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var allUsers = await context.Users.ToListAsync();
        var usersWithRoles = new List<UserWithRolesViewModel>();
        foreach (var user in allUsers)
        {
            usersWithRoles.Add(new UserWithRolesViewModel
            {
                User = user,
                Roles = await userManager.GetRolesAsync(user)
            });
        }

        return this.StackView(new IndexViewModel
        {
            Users = usersWithRoles
        });
    }

    [Authorize(Policy = AppPermissionNames.CanReadUsers)]
    public async Task<IActionResult> Details(string? id)
    {
        if (id == null) return NotFound();
        var user = await userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roleNames = await userManager.GetRolesAsync(user);
        var roles = await roleManager.Roles
            .Where(r => roleNames.Contains(r.Name!))
            .ToListAsync();

        var allPermissionValues = new HashSet<string>();
        foreach (var role in roles)
        {
            var claims = await roleManager.GetClaimsAsync(role);
            foreach (var claim in claims.Where(c => c.Type == AppPermissions.Type))
            {
                allPermissionValues.Add(claim.Value);
            }
        }

        var permissions = AppPermissions.GetAllPermissions()
            .Where(p => allPermissionValues.Contains(p.Key))
            .OrderBy(p => p.Name)
            .ToList();

        return this.StackView(new DetailsViewModel
        {
            User = user,
            Roles = roles,
            Permissions = permissions
        });
    }

    [Authorize(Policy = AppPermissionNames.CanAddUsers)]
    public IActionResult Create()
    {
        return this.StackView(new CreateViewModel());
    }

    [Authorize(Policy = AppPermissionNames.CanAddUsers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel newUser)
    {
        if (ModelState.IsValid)
        {
            var user = new User
            {
                UserName = newUser.UserName,
                DisplayName = newUser.DisplayName,
                Email = newUser.Email,
            };
            var result = await userManager.CreateAsync(user, newUser.Password!);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return this.StackView(newUser);
            }

            return RedirectToAction(nameof(Details), new { id = user.Id });
        }
        return this.StackView(newUser);
    }

    // GET: Users/Edit/5
    [Authorize(Policy = AppPermissionNames.CanEditUsers)]
    public async Task<IActionResult> Edit(string? id)
    {
        if (id == null) return NotFound();
        var user = await userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var userRoles = await userManager.GetRolesAsync(user);
        var allRoles = await roleManager.Roles.ToListAsync();

        var model = new EditViewModel
        {
            Id = id,
            Email = user.Email!,
            UserName = user.UserName!,
            DisplayName = user.DisplayName,
            Password = "you-cant-read-it",
            AvatarUrl = user.AvatarRelativePath,
            AllRoles = allRoles.Select(role => new UserRoleViewModel
            {
                RoleName = role.Name!,
                // 如果用户拥有该角色，则 IsSelected 为 true
                IsSelected = userRoles.Contains(role.Name!)
            }).ToList()
        };

        return this.StackView(model);
    }

    // POST: /Users/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditUsers)]
    public async Task<IActionResult> Edit(EditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }
        var userInDb = await userManager.FindByIdAsync(model.Id);
        if (userInDb == null) return NotFound();

        userInDb.Email = model.Email;
        userInDb.UserName = model.UserName;
        userInDb.DisplayName = model.DisplayName;
        userInDb.AvatarRelativePath = model.AvatarUrl;
        await userManager.UpdateAsync(userInDb);

        if (!string.IsNullOrWhiteSpace(model.Password) && model.Password != "you-cant-read-it")
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(userInDb);
            await userManager.ResetPasswordAsync(userInDb, token, model.Password);
        }

        return RedirectToAction(nameof(Details), new { id = userInDb.Id });
    }

    // POST: /Users/ManageRoles/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanAssignRoleToUser)]
    public async Task<IActionResult> ManageRoles(string id, EditViewModel model)
    {
        var userInDb = await userManager.FindByIdAsync(id);
        if (userInDb == null) return NotFound();

        var userCurrentRoles = await userManager.GetRolesAsync(userInDb);
        var selectedRoles = model
            .AllRoles
            .Where(r => r.IsSelected)
            .Select(r => r.RoleName)
            .ToArray();

        var rolesToAdd = selectedRoles.Except(userCurrentRoles);
        await userManager.AddToRolesAsync(userInDb, rolesToAdd);

        var rolesToRemove = userCurrentRoles.Except(selectedRoles);
        await userManager.RemoveFromRolesAsync(userInDb, rolesToRemove);

        return RedirectToAction(nameof(Details), new { id = userInDb.Id });
    }

    // GET: Users/Delete/5
    [Authorize(Policy = AppPermissionNames.CanDeleteUsers)]
    public async Task<IActionResult> Delete(string? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var user = await context.Users
            .FirstOrDefaultAsync(m => m.Id == id);
        if (user == null)
        {
            return NotFound();
        }

        return this.StackView(new DeleteViewModel
        {
            User = user,
        });
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanDeleteUsers)]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }
        await userManager.DeleteAsync(user);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// API: Search users by username or display name
    /// </summary>
    /// <param name="query">Search query (min 3 characters)</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Page size (1-20, default 10)</param>
    /// <returns>JSON object with users array and pagination info</returns>
    [HttpGet("/api/users/search")]
    public async Task<IActionResult> SearchUsers(string query, int page = 1, int pageSize = 10)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
        {
            return Json(new { users = new List<object>(), totalCount = 0, page, pageSize });
        }

        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1 || pageSize > 20)
        {
            pageSize = 10;
        }

        var queryable = context.Users
            .Where(u => u.UserName!.Contains(query) || u.DisplayName.Contains(query));

        var totalCount = await queryable.CountAsync();

        var users = await queryable
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var userList = users.Select(u => new
        {
            id = u.Id,
            userName = u.UserName,
            displayName = u.DisplayName,
            avatarUrl = storageService.RelativePathToInternetUrl(u.AvatarRelativePath) + "?w=128&square=true"
        });

        return Json(new
        {
            users = userList,
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }
}
