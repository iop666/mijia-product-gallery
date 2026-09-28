using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;

namespace MijiaProductGallery.ViewModels;

/// <summary>筛选面板单个可选项。</summary>
public sealed partial class FilterOption : ObservableObject
{
    public FilterOption(string label)
    {
        Label = label;
    }

    public string Label { get; }

    [ObservableProperty]
    private bool isSelected;
}

/// <summary>在架状态单选。</summary>
public enum AvailabilityOption
{
    All,

    /// <summary>仅在售。</summary>
    AvailableOnly,

    /// <summary>仅已下架。</summary>
    DelistedOnly,
}

/// <summary>图片状态单选。</summary>
public enum ImageOption
{
    All,
    WithImage,
    WithoutImage,
}

/// <summary>
/// 右侧筛选面板视图模型：分类/品牌多选，状态/图片/使用/更新时间单选；
/// BuildFilter 产出与 UI 无关的 ProductFilter，选择变化即时持久化到 AppSettings。
/// </summary>
public partial class FilterPaneViewModel : ObservableObject
{
    private const string SettingsKey = "Gallery.FilterState";

    private readonly ISettingsRepository? settings;
    private bool suppressEvents;

    public FilterPaneViewModel(ISettingsRepository? settings = null)
    {
        this.settings = settings;
    }

    /// <summary>任何筛选维度变化（恢复后的人工变更）。</summary>
    public event Action? FilterChanged;

    public ObservableCollection<FilterOption> Categories { get; } = [];

    public ObservableCollection<FilterOption> Brands { get; } = [];

    [ObservableProperty]
    private AvailabilityOption availability = AvailabilityOption.All;

    [ObservableProperty]
    private ImageOption imageOption = ImageOption.All;

    [ObservableProperty]
    private UsageRange usage = UsageRange.None;

    [ObservableProperty]
    private DateRange updateRange = DateRange.All;

    /// <summary>面板是否展开。</summary>
    [ObservableProperty]
    private bool isOpen;

    partial void OnAvailabilityChanged(AvailabilityOption value) => RaiseFilterChanged();

    partial void OnImageOptionChanged(ImageOption value) => RaiseFilterChanged();

    partial void OnUsageChanged(UsageRange value) => RaiseFilterChanged();

    partial void OnUpdateRangeChanged(DateRange value) => RaiseFilterChanged();

    /// <summary>加载可选项（分类/品牌全量）并恢复上次保存的筛选状态。</summary>
    public async Task LoadAsync(
        IProductRepository repository,
        ISettingsRepository settingsRepository,
        CancellationToken cancellationToken = default)
    {
        var categories = await repository.GetCategoriesAsync(cancellationToken);
        var brands = await repository.GetBrandsAsync(cancellationToken);

        var saved = await settingsRepository.GetValueAsync(SettingsKey, new FilterStateDto(), cancellationToken);
        var savedCategories = saved.Categories;
        var savedBrands = saved.Brands;

        suppressEvents = true;
        FillOptions(Categories, categories, savedCategories);
        FillOptions(Brands, brands, savedBrands);
        Availability = saved?.Availability is true
            ? AvailabilityOption.AvailableOnly
            : saved?.Availability is false ? AvailabilityOption.DelistedOnly : AvailabilityOption.All;
        ImageOptionValue = saved?.HasImage is true
            ? ImageOption.WithImage
            : saved?.HasImage is false ? ImageOption.WithoutImage : ImageOption.All;
        Usage = Enum.TryParse(saved?.Usage, out UsageRange usage) ? usage : UsageRange.None;
        UpdateRange = Enum.TryParse(saved?.Date, out DateRange date) ? date : DateRange.All;
        suppressEvents = false;
    }

    /// <summary>图片单选的绑定包装（ImageOption 与筛选字段解耦命名）。</summary>
    public ImageOption ImageOptionValue
    {
        get => ImageOption;
        set => ImageOption = value;
    }

    /// <summary>由当前选择构建 ProductFilter；全部默认值时返回 null。</summary>
    public ProductFilter? BuildFilter()
    {
        if (IsEmpty)
        {
            return null;
        }

        return new ProductFilter
        {
            Categories = Selected(Categories),
            Brands = Selected(Brands),
            IsAvailable = Availability switch
            {
                AvailabilityOption.AvailableOnly => true,
                AvailabilityOption.DelistedOnly => false,
                _ => null,
            },
            HasImage = ImageOptionValue switch
            {
                ImageOption.WithImage => true,
                ImageOption.WithoutImage => false,
                _ => null,
            },
            Usage = Usage,
            UpdateTime = UpdateRange,
        };
    }

    /// <summary>是否没有任何筛选。</summary>
    public bool IsEmpty =>
        Selected(Categories).Count == 0
        && Selected(Brands).Count == 0
        && Availability == AvailabilityOption.All
        && ImageOptionValue == ImageOption.All
        && Usage is UsageRange.None
        && UpdateRange == DateRange.All;

    /// <summary>设置某个分类选项的选中态（供 Chip 反向删除）。</summary>
    public void SetCategorySelected(string label, bool isSelected)
    {
        var option = Categories.FirstOrDefault(option => option.Label == label);
        if (option is not null)
        {
            option.IsSelected = isSelected;
        }
    }

    /// <summary>设置某个品牌选项的选中态（供 Chip 反向删除）。</summary>
    public void SetBrandSelected(string label, bool isSelected)
    {
        var option = Brands.FirstOrDefault(option => option.Label == label);
        if (option is not null)
        {
            option.IsSelected = isSelected;
        }
    }

    /// <summary>清空全部筛选（单次变更通知）。</summary>
    public void Reset()
    {
        suppressEvents = true;
        foreach (var option in Categories)
        {
            option.IsSelected = false;
        }

        foreach (var option in Brands)
        {
            option.IsSelected = false;
        }

        Availability = AvailabilityOption.All;
        ImageOptionValue = ImageOption.All;
        Usage = UsageRange.None;
        UpdateRange = DateRange.All;
        suppressEvents = false;
        RaiseFilterChanged();
    }

    private static List<string> Selected(ObservableCollection<FilterOption> options)
    {
        return options.Where(option => option.IsSelected).Select(option => option.Label).ToList();
    }

    private void FillOptions(
        ObservableCollection<FilterOption> target,
        IReadOnlyList<string> values,
        IReadOnlyList<string>? selectedLabels)
    {
        var selectedSet = selectedLabels is null ? null : new HashSet<string>(selectedLabels, StringComparer.Ordinal);
        target.Clear();
        foreach (var value in values)
        {
            var option = new FilterOption(value);
            if (selectedSet?.Contains(value) == true)
            {
                option.IsSelected = true;
            }

            target.Add(option);
            option.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(FilterOption.IsSelected))
                {
                    RaiseFilterChanged();
                }
            };
        }
    }

    private void RaiseFilterChanged()
    {
        if (suppressEvents)
        {
            return;
        }

        FilterChanged?.Invoke();
        _ = PersistSafeAsync();
    }

    private async Task PersistSafeAsync()
    {
        if (settings is null)
        {
            return;
        }

        try
        {
            var empty = IsEmpty;
            await settings.SetValueAsync(
                SettingsKey,
                empty
                    ? new FilterStateDto()
                    : new FilterStateDto
                    {
                        Categories = Selected(Categories),
                        Brands = Selected(Brands),
                        Availability = Availability == AvailabilityOption.AvailableOnly
                            ? true
                            : Availability == AvailabilityOption.DelistedOnly ? false : null,
                        HasImage = ImageOptionValue == ImageOption.WithImage
                            ? true
                            : ImageOptionValue == ImageOption.WithoutImage ? false : null,
                        Usage = Usage.ToString(),
                        Date = UpdateRange.ToString(),
                    });
        }
        catch
        {
            // 持久化失败不影响筛选行为。
        }
    }

    /// <summary>持久化形态（与架构文档 §7 示例一致的 camelCase JSON）。</summary>
    public sealed class FilterStateDto
    {
        [JsonPropertyName("categories")]
        public IReadOnlyList<string>? Categories { get; set; }

        [JsonPropertyName("brands")]
        public IReadOnlyList<string>? Brands { get; set; }

        [JsonPropertyName("availability")]
        public bool? Availability { get; set; }

        [JsonPropertyName("hasImage")]
        public bool? HasImage { get; set; }

        [JsonPropertyName("usage")]
        public string? Usage { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }
    }
}
