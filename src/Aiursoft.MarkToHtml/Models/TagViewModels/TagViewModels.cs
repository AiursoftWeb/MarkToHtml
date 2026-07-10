using System.ComponentModel.DataAnnotations;

namespace Aiursoft.MarkToHtml.Models.TagViewModels;

public class CreateTagViewModel
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Color { get; set; }
}

public class EditTagViewModel
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Color { get; set; }
}

public class DeleteTagViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
}

public class IndexTagViewModel
{
    public List<Tag> Tags { get; set; } = new();
}

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int DocumentCount { get; set; }
}