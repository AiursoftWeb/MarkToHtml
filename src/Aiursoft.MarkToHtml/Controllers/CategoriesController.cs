using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Models.CategoryViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Controllers;

[Authorize]
public class CategoriesController(
    TemplateDbContext context,
    UserManager<User> userManager) : Controller
{
    /// <summary>
    /// GET: Show all categories for the current user.
    /// </summary>
    public async Task<IActionResult> Index([FromQuery] int? categoryId)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var parentCategory = categoryId.HasValue
            ? await context.Categories
                .FirstOrDefaultAsync(c => c.Id == categoryId.Value && c.UserId == userId)
            : null;

        if (categoryId.HasValue && parentCategory == null)
        {
            return NotFound();
        }

        // Get all sub-categories
        var categories = await context.Categories
            .Where(c => c.ParentCategoryId == categoryId && c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Count documents in each category
        var categoriesWithCounts = new List<Category>();
        foreach (var cat in categories)
        {
            var docCount = await context.DocumentCategories
                .CountAsync(dc => dc.CategoryId == cat.Id);
            cat.DocumentCount = docCount;
            categoriesWithCounts.Add(cat);
        }

        var model = new IndexCategoryViewModel
        {
            Categories = categoriesWithCounts,
            ParentCategory = parentCategory,
            Breadcrumb = await GetCategoryBreadcrumbAsync(categoryId, userId)
        };

        return View(model);
    }

    /// <summary>
    /// GET: Show create category form.
    /// </summary>
    public async Task<IActionResult> CreateCategory(int? id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!await IsCategoryOwnedByUser(id, userId))
        {
            return NotFound();
        }

        return View(new CreateCategoryViewModel { ParentCategoryId = id });
    }

    /// <summary>
    /// POST: Create a new category.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(CreateCategoryViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!await IsCategoryOwnedByUser(model.ParentCategoryId, userId))
        {
            return NotFound();
        }

        // Check for duplicate category name at the same level
        var nameExists = await context.Categories
            .AnyAsync(c => c.UserId == userId
                         && c.ParentCategoryId == model.ParentCategoryId
                         && c.Name == model.Name);
        if (nameExists)
        {
            ModelState.AddModelError(nameof(model.Name), "A category with this name already exists in this location.");
            return View(model);
        }

        var category = new Category
        {
            Name = model.Name,
            Description = model.Description,
            ParentCategoryId = model.ParentCategoryId,
            UserId = userId
        };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return RedirectToAction("Index", new { categoryId = model.ParentCategoryId });
    }

    /// <summary>
    /// GET: Show delete confirmation page.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var category = await context.Categories
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

        if (category == null) return NotFound();

        // Count documents in this category
        var directDocs = await context.DocumentCategories
            .CountAsync(dc => dc.CategoryId == id);

        // Count direct sub-categories
        var directSubCats = await context.Categories
            .CountAsync(c => c.ParentCategoryId == id && c.UserId == userId);

        // Count all descendant documents recursively
        var allSubCatIds = new List<int>();
        await CollectDescendantCategoryIds(id, allSubCatIds);
        var recursiveDocCount = await context.DocumentCategories
            .CountAsync(dc => allSubCatIds.Contains(dc.CategoryId));

        var model = new DeleteCategoryViewModel
        {
            Id = category.Id,
            Name = category.Name,
            ParentCategoryId = category.ParentCategoryId,
            DirectDocumentCount = directDocs,
            DirectSubCategoryCount = directSubCats,
            RecursiveDocumentCount = recursiveDocCount,
            RecursiveSubCategoryCount = allSubCatIds.Count
        };

        return View(model);
    }

    /// <summary>
    /// POST: Delete category and all its contents recursively.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName("DeleteCategory")]
    public async Task<IActionResult> DeleteCategoryConfirmed(int id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var category = await context.Categories
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

        if (category == null) return NotFound();

        // Collect all descendant category IDs
        var allCatIds = new List<int>();
        await CollectDescendantCategoryIds(id, allCatIds);

        // Delete all document-category links in these categories
        var docCatsToDelete = await context.DocumentCategories
            .Where(dc => allCatIds.Contains(dc.CategoryId))
            .ToListAsync();
        context.DocumentCategories.RemoveRange(docCatsToDelete);

        // Delete sub-categories bottom-up
        var catsToDelete = await context.Categories
            .Where(c => c.UserId == userId && allCatIds.Contains(c.Id))
            .OrderByDescending(c => c.Id)
            .ToListAsync();
        context.Categories.RemoveRange(catsToDelete);

        await context.SaveChangesAsync();

        return RedirectToAction("Index", new { categoryId = category.ParentCategoryId });
    }

    private async Task<List<Category>> GetCategoryBreadcrumbAsync(int? categoryId, string userId)
    {
        var breadcrumb = new List<Category>();
        if (!categoryId.HasValue) return breadcrumb;

        var current = await context.Categories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.UserId == userId);

        while (current != null)
        {
            breadcrumb.Insert(0, new Category { Id = current.Id, Name = current.Name });
            current = current.ParentCategoryId.HasValue
                ? await context.Categories
                    .FirstOrDefaultAsync(c => c.Id == current.ParentCategoryId.Value && c.UserId == userId)
                : null;
        }

        return breadcrumb;
    }

    private async Task CollectDescendantCategoryIds(int categoryId, List<int> result)
    {
        var childIds = await context.Categories
            .Where(c => c.ParentCategoryId == categoryId)
            .Select(c => c.Id)
            .ToListAsync();

        foreach (var childId in childIds)
        {
            result.Add(childId);
            await CollectDescendantCategoryIds(childId, result);
        }
    }

    private async Task<bool> IsCategoryOwnedByUser(int? categoryId, string userId)
    {
        return categoryId == null || await context.Categories
            .AnyAsync(c => c.Id == categoryId.Value && c.UserId == userId);
    }
}