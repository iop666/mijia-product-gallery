using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App.Views;

/// <summary>收藏夹管理视图：列表单选、内联输入区新建/重命名；删除确认由宿主页协调。</summary>
public sealed partial class CollectionManagerView : UserControl
{
    private readonly GalleryViewModel vm;
    private bool inputIsRename;
    private int renameTargetId;

    /// <summary>请求删除收藏夹（宿主页弹出确认框后执行）。</summary>
    public event EventHandler<(int Id, string Name)>? DeleteRequested;

    public ObservableCollection<CollectionRow> Rows { get; } = [];

    public CollectionManagerView(GalleryViewModel viewModel, int? initialSelectedId = null)
    {
        InitializeComponent();
        vm = viewModel;
        _ = ReloadRowsAsync(initialSelectedId ?? vm.SelectedCollection?.Id ?? 0);
        List.Loaded += (_, _) => List.Focus(FocusState.Programmatic);
    }

    private async Task ReloadRowsAsync(int selectId)
    {
        Rows.Clear();
        foreach (var option in vm.Collections)
        {
            Rows.Add(new CollectionRow(option.Id, option.Name, option.IsDefault));
        }

        var target = Rows.FirstOrDefault(row => row.Id == selectId) ?? Rows.FirstOrDefault();
        if (target is not null)
        {
            List.SelectedIndex = Rows.IndexOf(target);
        }

        UpdateActionStates();
        await Task.CompletedTask;
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActionStates();
        ClearError();
    }

    private CollectionRow? Selected() => List.SelectedItem as CollectionRow;

    private void UpdateActionStates()
    {
        var row = Selected();
        var isNormal = row is { IsDefault: false };
        RenameButton.IsEnabled = isNormal;
        DeleteButton.IsEnabled = isNormal;
        ToolTipService.SetToolTip(RenameButton, isNormal ? "重命名选中的收藏夹" : "默认收藏不可重命名，请选择一个自建收藏夹");
        ToolTipService.SetToolTip(DeleteButton, isNormal ? "删除选中的收藏夹（不影响其中产品）" : "默认收藏不可删除");
        AutomationProperties.SetName(RenameButton, "重命名收藏夹");
        AutomationProperties.SetName(DeleteButton, "删除收藏夹");
    }

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row || row.IsDefault)
        {
            return;
        }

        inputIsRename = true;
        renameTargetId = row.Id;
        ShowInput(row.Name, "保存", "Enter 保存，Esc 取消");
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row || row.IsDefault)
        {
            return;
        }

        DeleteRequested?.Invoke(this, (row.Id, row.Name));
    }

    private void OnCreateStartClick(object sender, RoutedEventArgs e)
    {
        inputIsRename = false;
        ShowInput(string.Empty, "创建", "Enter 创建，Esc 取消");
    }

    private void ShowInput(string initialText, string confirmText, string hint)
    {
        CreateButton.Visibility = Visibility.Collapsed;
        InputPanel.Visibility = Visibility.Visible;
        InputBox.Text = initialText;
        InputConfirmButton.Content = confirmText;
        InputHint.Text = hint;
        InputHint.Visibility = Visibility.Visible;
        ClearError();
        _ = InputBox.Focus(FocusState.Programmatic);
        InputBox.SelectAll();
    }

    private void ExitInput()
    {
        InputPanel.Visibility = Visibility.Collapsed;
        CreateButton.Visibility = Visibility.Visible;
        InputHint.Visibility = Visibility.Collapsed;
        ClearError();
        _ = List.Focus(FocusState.Programmatic);
    }

    private async void OnInputConfirmClick(object sender, RoutedEventArgs e)
    {
        await ConfirmInputAsync();
    }

    private async void OnInputBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await ConfirmInputAsync();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            ExitInput();
        }
    }

    private void OnInputCancelClick(object sender, RoutedEventArgs e)
    {
        ExitInput();
    }

    private async Task ConfirmInputAsync()
    {
        var name = InputBox.Text;
        var (success, error) = inputIsRename
            ? await vm.RenameCollectionAsync(renameTargetId, name)
            : await vm.CreateCollectionAsync(name);
        if (!success)
        {
            ShowError(error ?? "操作失败");
            return;
        }

        var selectId = inputIsRename ? renameTargetId : vm.SelectedCollection?.Id ?? 0;
        inputIsRename = false;
        await ReloadRowsAsync(selectId);
        ExitInput();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ClearError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }
}

/// <summary>收藏夹列表行（默认收藏不可重命名/删除，图标为星标）。</summary>
public sealed class CollectionRow
{
    public CollectionRow(int id, string name, bool isDefault)
    {
        Id = id;
        Name = name;
        IsDefault = isDefault;
    }

    public int Id { get; }

    public string Name { get; }

    public bool IsDefault { get; }

    public bool IsNormal => !IsDefault;
}
