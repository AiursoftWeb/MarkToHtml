using Aiursoft.MarkToHtml.Models;

namespace Aiursoft.MarkToHtml.Configuration;

public class SettingsMap
{
    public const string ProjectName = "ProjectName";
    public const string BrandName = "BrandName";
    public const string BrandHomeUrl = "BrandHomeUrl";
    public const string ProjectLogo = "ProjectLogo";
    public const string AllowUserAdjustNickname = "Allow_User_Adjust_Nickname";
    public const string Icp = "Icp";
    public const string CompanyAddress = "CompanyAddress";
    public const string CompanyPhone = "CompanyPhone";
    public const string CompanyEmail = "CompanyEmail";
    public const string CompanyPostcode = "CompanyPostcode";
    public const string ContractLogo = "ContractLogo";
    public const string ShowContractHeader = "ShowContractHeader";

    // ── AI: Embedding / Vector Search ──────────────────────────────────────────
    public const string EmbeddingEndpoint = "EmbeddingEndpoint";
    public const string EmbeddingModel = "EmbeddingModel";
    public const string EmbeddingApiToken = "EmbeddingApiToken";
    public const string EnableEmbeddingBasedSearch = "EnableEmbeddingBasedSearch";
    public const string EmbeddingQueryCacheLimit = "EmbeddingQueryCacheLimit";

    // ── AI: Agent / Editor Assistant ───────────────────────────────────────────
    public const string AgentApiEndpoint = "AgentApiEndpoint";
    public const string AgentApiModel = "AgentApiModel";
    public const string AgentApiToken = "AgentApiToken";
    public const string AgentSystemPrompt = "AgentSystemPrompt";

    public class FakeLocalizer
    {
        public string this[string name] => name;
    }

    private static readonly FakeLocalizer Localizer = new();

    public static readonly List<GlobalSettingDefinition> Definitions = new()
    {
        new GlobalSettingDefinition
        {
            Key = ProjectName,
            Name = Localizer["Project Name"],
            Description = Localizer["The name of the project displayed in the frontend."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft MarkToHtml"
        },
        new GlobalSettingDefinition
        {
            Key = BrandName,
            Name = Localizer["Brand Name"],
            Description = Localizer["The brand name of the company or project. E.g. Aiursoft."],
            Type = SettingType.Text,
            DefaultValue = "Aiursoft"
        },
        new GlobalSettingDefinition
        {
            Key = BrandHomeUrl,
            Name = Localizer["Brand Home URL"],
            Description = Localizer["The URL of the company or project. E.g. https://www.aiursoft.com"],
            Type = SettingType.Text,
            DefaultValue = "https://www.aiursoft.com"
        },
        new GlobalSettingDefinition
        {
            Key = ProjectLogo,
            Name = Localizer["Project Logo"],
            Description = Localizer["The logo of the project displayed in the navbar and footer. Support jpg, png, svg."],
            Type = SettingType.File,
            DefaultValue = "",
            Subfolder = "project-logo",
            AllowedExtensions = "jpg png svg",
            MaxSizeInMb = 5
        },
        new GlobalSettingDefinition
        {
            Key = AllowUserAdjustNickname,
            Name = Localizer["Allow User Adjust Nickname"],
            Description = Localizer["Allow users to adjust their nickname in the profile management page."],
            Type = SettingType.Bool,
            DefaultValue = "True"
        },
        new GlobalSettingDefinition
        {
            Key = Icp,
            Name = Localizer["ICP Number"],
            Description = Localizer["The ICP license number for China mainland users. Leave empty to hide."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = CompanyAddress,
            Name = Localizer["Company Address"],
            Description = Localizer["The address of the company or project."],
            Type = SettingType.Text,
            DefaultValue = "西京市中关村大街 999 号"
        },
        new GlobalSettingDefinition
        {
            Key = CompanyPhone,
            Name = Localizer["Company Phone"],
            Description = Localizer["The phone number of the company or project."],
            Type = SettingType.Text,
            DefaultValue = "010-12345678"
        },
        new GlobalSettingDefinition
        {
            Key = CompanyEmail,
            Name = Localizer["Company Email"],
            Description = Localizer["The email address of the company or project."],
            Type = SettingType.Text,
            DefaultValue = "anduin@aiursoft.com"
        },
        new GlobalSettingDefinition
        {
            Key = CompanyPostcode,
            Name = Localizer["Company Postcode"],
            Description = Localizer["The postcode of the company or project."],
            Type = SettingType.Text,
            DefaultValue = "100080"
        },
        new GlobalSettingDefinition
        {
            Key = ContractLogo,
            Name = Localizer["Contract Logo"],
            Description = Localizer["The logo of the contract displayed in the header. Support jpg, png, svg. Separate from system logo."],
            Type = SettingType.File,
            DefaultValue = "",
            Subfolder = "contract-logo",
            AllowedExtensions = "jpg png svg",
            MaxSizeInMb = 5
        },
        new GlobalSettingDefinition
        {
            Key = ShowContractHeader,
            Name = Localizer["Show Contract Header"],
            Description = Localizer["Whether to show the contract header (Logo, address, etc.) in the contract view."],
            Type = SettingType.Bool,
            DefaultValue = "True"
        },
        new GlobalSettingDefinition
        {
            Key = EmbeddingEndpoint,
            Name = Localizer["Embedding Endpoint"],
            Description = Localizer["Ollama API base URL for generating document and query embeddings (e.g. https://ollama.example.com). /api/embed is appended automatically."],
            Type = SettingType.Text,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = EmbeddingModel,
            Name = Localizer["Embedding Model"],
            Description = Localizer["Embedding model name for vector search, e.g. bge-m3:latest."],
            Type = SettingType.Text,
            DefaultValue = "bge-m3:latest"
        },
        new GlobalSettingDefinition
        {
            Key = EmbeddingApiToken,
            Name = Localizer["Embedding API Token"],
            Description = Localizer["Bearer token for the Embedding Endpoint. Leave empty if no auth required."],
            Type = SettingType.Secret,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = EnableEmbeddingBasedSearch,
            Name = Localizer["Enable Embedding-Based Search"],
            Description = Localizer["Master switch for semantic (vector-based) search. Falls back to keyword search when disabled."],
            Type = SettingType.Bool,
            DefaultValue = "False"
        },
        new GlobalSettingDefinition
        {
            Key = EmbeddingQueryCacheLimit,
            Name = Localizer["Embedding Query Cache Limit"],
            Description = Localizer["Maximum number of cached search-query embeddings stored in the database (LRU). Default 2000."],
            Type = SettingType.Number,
            DefaultValue = "2000"
        },
        // ── AI Agent Settings ──────────────────────────────────────────────────
        new GlobalSettingDefinition
        {
            Key = AgentApiEndpoint,
            Name = Localizer["Agent API Endpoint"],
            Description = Localizer["Anthropic Messages API endpoint for the AI editor assistant (e.g. https://api.anthropic.com/v1/messages). Leave empty to disable."],
            Type = SettingType.Text,
            DefaultValue = "https://ollama.aiursoft.com/v1/messages"
        },
        new GlobalSettingDefinition
        {
            Key = AgentApiModel,
            Name = Localizer["Agent API Model"],
            Description = Localizer["Model name for the AI editor assistant, e.g. claude-sonnet-5."],
            Type = SettingType.Text,
            DefaultValue = "aiursoft-instruct:latest"
        },
        new GlobalSettingDefinition
        {
            Key = AgentApiToken,
            Name = Localizer["Agent API Token"],
            Description = Localizer["API token (x-api-key header) for the Agent API Endpoint."],
            Type = SettingType.Secret,
            DefaultValue = ""
        },
        new GlobalSettingDefinition
        {
            Key = AgentSystemPrompt,
            Name = Localizer["Agent System Prompt"],
            Description = Localizer["System prompt for the AI editor assistant. Use {documentTitle} and {documentContentLength} as placeholders."],
            Type = SettingType.Text,
            DefaultValue = "You are an AI editing assistant for a Markdown document editor. Your role is to help the user edit their document based on their feedback.\n\nUse a single line of plain text for your message reply. Do not use markdown format. Do not use ** and other markdown mark servers.\n\n## Guidelines\n1. FIRST read the relevant parts of the document before proposing edits\n2. Understand the user's feedback thoroughly\n3. Each edit should be minimal and focused\n4. Preserve the document's existing style, formatting, and voice\n5. For complex changes, break them into multiple small ReplaceText calls\n6. If the user rejects your edit, do NOT retry - ask what they want differently"
        }
    };
}
