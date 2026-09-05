namespace AiEmailer.Api.Models
{
    public record MenuListItem(
    int IdMenu,
    string MenuName,
    string MenuKey,
    string? MenuScript,
    string? IconClass,
    int? IdParentMenu,
    int MenuLevel,
    int OrderId,
    bool IsEnable
    );

    public record MenuAddUpdRequest(
        int IdMenu,
        string MenuName,
        string MenuKey,
        string? MenuScript,
        string? IconClass,
        int? IdParentMenu,
        int MenuLevel,
        int OrderId,
        bool IsEnable
    );
}
