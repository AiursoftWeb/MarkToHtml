using System.ComponentModel.DataAnnotations;

namespace Aiursoft.MarkToHtml.Models.CategoryViewModels;

public class CreateCategoryViewModel
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? ParentCategoryId { get; set; }
}

public class EditCategoryViewModel
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? ParentCategoryId { get; set; }
    public int? BrowseParentCategoryId { get; set; }
}

public class DeleteCategoryViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentCategoryId { get; set; }
    public int DirectDocumentCount { get; set; }
    public int DirectSubCategoryCount { get; set; }
    public int RecursiveDocumentCount { get; set; }
    public int RecursiveSubCategoryCount { get; set; }
}

public class IndexCategoryViewModel
{
    public List<Category> Categories { get; set; } = new();
    public Category? ParentCategory { get; set; }
    public List<Category> Breadcrumb { get; set; } = new();
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? ParentCategoryId { get; set; }
    public int DocumentCount { get; set; }
}