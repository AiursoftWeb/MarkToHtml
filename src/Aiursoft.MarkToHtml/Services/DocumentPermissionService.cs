using System.Security.Claims;
using Aiursoft.MarkToHtml.Authorization;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Services;

public class DocumentPermissionService(
    TemplateDbContext dbContext,
    IAuthorizationService authorizationService) : IScopedDependency
{
    public async Task<bool> CanReadAsync(ClaimsPrincipal user, MarkdownDocument document)
    {
        if (document.IsPublic)
        {
            return true;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return false;
        }

        if (document.UserId == userId)
        {
            return true;
        }

        var userRoles = await dbContext.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        return await dbContext.DocumentShares
            .AnyAsync(s => s.DocumentId == document.Id &&
                          (s.SharedWithUserId == userId ||
                           (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
    }

    public async Task<bool> CanEditAsync(ClaimsPrincipal user, MarkdownDocument document)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return false;
        }

        if (document.UserId == userId)
        {
            return true;
        }

        var userRoles = await dbContext.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        return await dbContext.DocumentShares
            .AnyAsync(s => s.DocumentId == document.Id &&
                          s.Permission == SharePermission.Editable &&
                          (s.SharedWithUserId == userId ||
                           (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
    }

    public async Task<bool> CanManageAsync(ClaimsPrincipal user, MarkdownDocument document)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return false;
        }

        if (document.UserId == userId)
        {
            return true;
        }

        var canManageAny = (await authorizationService.AuthorizeAsync(user, AppPermissionNames.CanManageAnyShare)).Succeeded;
        return canManageAny;
    }
}
