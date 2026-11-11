using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using PayorContracts.Web.Models;
using PayorContracts.Web.Services;

namespace PayorContracts.Web.Components.Pages;

/// <summary>
/// Code-behind of the PayorList page (migrated from PayorList.aspx): filter bar, server-paged
/// grid, click-to-edit cells with changed-row tracking and the Save All / Cancel action bar.
/// </summary>
public partial class PayorList
{
    /// <summary>Which cell editor is open.</summary>
    public enum EditField
    {
        None,
        Name,
        Code,
        Category,
        EffectiveDate
    }

    public const string DateFormat = "MM/dd/yyyy";
    public const string EmptyCell = "\u2014";
    public const int DefaultPageSize = PayorSearchRequest.DefaultPageSize;
    private const int ToastDurationMs = 3000;
    private const int ToastFadeMs = 300;
    private const string SaveErrorText = "Error saving changes: service unavailable";
    private const string AccessDeniedText = "You are not allowed to modify payors.";

    /// <summary>Category options of the inline select (Reference categories array).</summary>
    public static readonly IReadOnlyList<string> Categories = new[] { "Commercial", "Medicare", "Medicaid", "Tricare" };

    /// <summary>Page-size options of the pager (Reference rowList).</summary>
    public static readonly int[] PageSizeOptions = { 5, 10, 15, 25 };

    public static readonly CultureInfo DateCulture = CultureInfo.InvariantCulture;

    [Inject] private PayorService PayorService { get; set; } = default!;
    [Inject] private PayorAccessGuard AccessGuard { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private ILogger<PayorList> Logger { get; set; } = default!;

    private MudDataGrid<PayorGridRow>? _grid;
    private MudDatePicker? _datePicker;
    private ElementReference _editorRef;

    // Filter state (Reference txtNameFilter / txtCodeFilter / ddlActiveFilter)
    private string _nameFilter = string.Empty;
    private string _codeFilter = string.Empty;
    private PayorStatusFilter _statusFilter = PayorStatusFilter.All;

    // Edit tracking (Reference originalData / editedValues / changedRows)
    private readonly Dictionary<int, PayorDto> _originalData = new();
    private readonly Dictionary<int, PayorGridRow> _editedRows = new();
    private readonly HashSet<int> _changedRows = new();

    private int _editingRowId;
    private EditField _editingField = EditField.None;
    private string _draft = string.Empty;
    private bool _focusEditor;
    private bool _isSaving;

    // Page-level authorization: edit UI is gated by PayorAccessGuard (the service guards again)
    private bool _canModify;

    private int ChangeCount => _changedRows.Count;

    private bool HasChanges => _changedRows.Count > 0;

    private string ChangeCountText => HasChanges ? $"{ChangeCount} unsaved change(s)" : "No unsaved changes";

    private string SaveButtonText => _isSaving ? "Saving..." : HasChanges ? $"Save All ({ChangeCount})" : "Save All";

    private bool IsSaveDisabled => _isSaving || !HasChanges || !_canModify;

    private bool IsCancelDisabled => !HasChanges;

    private bool IsStatusDisabled => _isSaving || !_canModify;

    // Pager state (Reference jqGrid pager: "View x - y of z", "No records to view", "Page [n] of N")
    private const string PagerInfoText = "View {first_item} - {last_item} of {all_items}";
    private const string EmptyRecordsText = "No records to view";
    private int _totalItems;
    private int _pageSize = DefaultPageSize;
    private string _pageDraft = string.Empty;

    private string PagerInfoFormat => _totalItems == 0 ? EmptyRecordsText : PagerInfoText;

    // "payor-grid-empty" hides the built-in "0-0 of 0" caption; the page renders the empty-records text instead
    private bool _isEmpty;

    private string GridClass => _isEmpty ? "payor-grid payor-grid-empty" : "payor-grid";

    /// <summary>Re-renders the page (pager caption, grid class) when the grid switches between empty and non-empty.</summary>
    private void SetEmpty(bool isEmpty)
    {
        if (_isEmpty == isEmpty)
            return;

        _isEmpty = isEmpty;
        _ = InvokeAsync(StateHasChanged);
    }

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(_totalItems / (double)Math.Max(1, _pageSize)));

    private string CurrentPageNumber => ((_grid?.CurrentPage ?? 0) + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Privilege check before the grid loads its data.</summary>
    protected override async Task OnInitializedAsync()
    {
        _canModify = await AccessGuard.IsAuthorizedToModifyAsync();
    }

    #region Server data

    /// <summary>
    /// MudDataGrid ServerData callback: maps grid page/size/sort and the filters onto SearchPayorsAsync,
    /// then overlays unsaved edits so modified rows stay edited and highlighted across reloads.
    /// </summary>
    private async Task<GridData<PayorGridRow>> LoadServerDataAsync(GridState<PayorGridRow> state)
    {
        var sort = state.SortDefinitions.FirstOrDefault();
        var request = new PayorSearchRequest
        {
            NameFilter = _nameFilter,
            CodeFilter = _codeFilter,
            ActiveOnly = _statusFilter switch
            {
                PayorStatusFilter.Active => true,
                PayorStatusFilter.Inactive => false,
                _ => null
            },
            SortColumn = sort?.SortBy ?? nameof(PayorGridRow.Id),
            SortDescending = sort?.Descending ?? false,
            Page = state.Page + 1,
            PageSize = state.PageSize > 0 ? state.PageSize : DefaultPageSize
        };

        try
        {
            var result = await PayorService.SearchPayorsAsync(request);
            var rows = new List<PayorGridRow>(result.Data.Count);
            foreach (var dto in result.Data)
            {
                // Keep the first-seen server values as the originals (Reference jsonReader.root)
                _originalData.TryAdd(dto.Id, dto);
                rows.Add(_editedRows.TryGetValue(dto.Id, out var edited) ? edited : PayorGridRow.FromDto(dto));
            }

            _totalItems = result.TotalRecords;
            _pageSize = request.PageSize;
            SetEmpty(result.TotalRecords == 0);
            return new GridData<PayorGridRow> { Items = rows, TotalItems = result.TotalRecords };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Loading payors failed");
            ShowToast("Error loading payors.", Severity.Error);
            _totalItems = 0;
            SetEmpty(true);
            return new GridData<PayorGridRow> { Items = Array.Empty<PayorGridRow>(), TotalItems = 0 };
        }
    }

    private void OnPageDraftInput(ChangeEventArgs e) => _pageDraft = e.Value?.ToString() ?? string.Empty;

    private Task OnPageInputChangedAsync(ChangeEventArgs e)
    {
        _pageDraft = e.Value?.ToString() ?? string.Empty;
        return GoToPageAsync();
    }

    private Task OnPageInputKeyDownAsync(KeyboardEventArgs e) =>
        e.Key is "Enter" or "NumpadEnter" ? GoToPageAsync() : Task.CompletedTask;

    /// <summary>Direct page box (Reference ui-pg-input): a valid 1-based page number navigates, anything else is ignored.</summary>
    private Task GoToPageAsync()
    {
        if (_grid is null
            || !int.TryParse(_pageDraft.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var page)
            || page < 1 || page > TotalPages)
            return Task.CompletedTask;

        var index = page - 1;
        if (_grid.CurrentPage != index)
        {
#pragma warning disable BL0005 // setting CurrentPage triggers the server reload (same path as NavigateTo)
            _grid.CurrentPage = index;
#pragma warning restore BL0005
        }

        return Task.CompletedTask;
    }

    private Task ReloadGridAsync() => _grid?.ReloadServerData() ?? Task.CompletedTask;

    /// <summary>Reloads at page 1 keeping page size and sort (Reference setGridParam page 1 + reloadGrid).</summary>
    private async Task ReloadFirstPageAsync()
    {
        if (_grid is null)
            return;

        if (_grid.CurrentPage != 0)
        {
            // Changing the page triggers the server reload
            _grid.NavigateTo(Page.First);
            return;
        }

        await _grid.ReloadServerData();
    }

    #endregion

    #region Filter bar

    private Task SearchAsync() => ReloadFirstPageAsync();

    /// <summary>Clear: empties Name and Code, Status = All, reloads page 1. Page size, sort and unsaved edits are kept.</summary>
    private Task ClearAsync()
    {
        _nameFilter = string.Empty;
        _codeFilter = string.Empty;
        _statusFilter = PayorStatusFilter.All;
        return ReloadFirstPageAsync();
    }

    private async Task OnFilterKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "NumpadEnter")
            await SearchAsync();
    }

    private static string StatusFilterText(PayorStatusFilter filter) => filter.ToString();

    #endregion

    #region Click-to-edit

    private bool IsEditing(PayorGridRow row, EditField field) => _editingField == field && _editingRowId == row.Id;

    private void BeginEdit(PayorGridRow row, EditField field)
    {
        if (_isSaving || !_canModify)
            return;

        _editingRowId = row.Id;
        _editingField = field;
        _draft = field switch
        {
            EditField.Name => row.Name,
            EditField.Code => row.Code,
            EditField.Category => string.IsNullOrEmpty(row.Category) ? Categories[0] : row.Category,
            _ => string.Empty
        };
        _focusEditor = true;
    }

    private void OnDisplayKeyDown(PayorGridRow row, EditField field, KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
            BeginEdit(row, field);
    }

    private void EndEdit()
    {
        _editingField = EditField.None;
        _editingRowId = 0;
        _draft = string.Empty;
    }

    private void OnDraftInput(ChangeEventArgs e) => _draft = e.Value?.ToString() ?? string.Empty;

    /// <summary>Enter commits, Esc reverts the cell to its original value (Reference keydown handlers).</summary>
    private void OnTextEditorKeyDown(PayorGridRow row, KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "NumpadEnter")
        {
            CommitText(row);
        }
        else if (e.Key == "Escape")
        {
            RevertText(row);
        }
    }

    /// <summary>Blur commits the text editor (Reference blur handlers).</summary>
    private void OnTextEditorBlur(PayorGridRow row)
    {
        if (_editingRowId == row.Id && _editingField is EditField.Name or EditField.Code)
            CommitText(row);
    }

    private void CommitText(PayorGridRow row)
    {
        if (_editingRowId != row.Id)
            return;

        var field = _editingField;
        var value = _draft;
        EndEdit();

        if (field == EditField.Name)
        {
            row.Name = value;
            StoreEdited(row);
            if (Original(row)?.Name != value)
                MarkRowChanged(row);
        }
        else if (field == EditField.Code)
        {
            row.Code = value;
            StoreEdited(row);
            if (Original(row)?.Code != value)
                MarkRowChanged(row);
        }
    }

    /// <summary>
    /// Esc shows the original value again and closes the editor. Reference parity: the legacy page
    /// still marks the row as changed after Esc (highlighted, counted, Save All enabled).
    /// </summary>
    private void RevertText(PayorGridRow row)
    {
        var original = Original(row);
        if (original is not null)
        {
            if (_editingField == EditField.Name)
                row.Name = original.Name;
            else if (_editingField == EditField.Code)
                row.Code = original.Code;
        }

        EndEdit();
        StoreEdited(row);
        MarkRowChanged(row);
    }

    /// <summary>Category select: change commits and closes the editor (Reference change -> blur).</summary>
    private void OnCategoryChanged(PayorGridRow row, ChangeEventArgs e)
    {
        var value = e.Value?.ToString() ?? string.Empty;
        EndEdit();
        row.Category = value;
        StoreEdited(row);
        if (Original(row)?.Category != value)
            MarkRowChanged(row);
    }

    private void OnCategoryBlur(PayorGridRow row)
    {
        if (!IsEditing(row, EditField.Category))
            return;

        // Blur without a change commits the shown value (Reference blur handler)
        OnCategoryChanged(row, new ChangeEventArgs { Value = _draft });
    }

    /// <summary>Date picker: typed and picked values both commit and mark the row changed when the date differs.</summary>
    private void OnDateChanged(PayorGridRow row, DateTime? date)
    {
        row.EffectiveDate = date?.Date;
        StoreEdited(row);
        if (FormatDate(Original(row)?.EffectiveDate) != FormatDate(row.EffectiveDate))
            MarkRowChanged(row);
    }

    private void OnDatePickerClosed()
    {
        if (_editingField == EditField.EffectiveDate)
            EndEdit();
    }

    /// <summary>Status checkbox (always editable): every change marks the row changed.</summary>
    private void OnStatusChanged(PayorGridRow row, ChangeEventArgs e)
    {
        if (!_canModify)
            return;

        row.IsActive = e.Value is bool b ? b : bool.TryParse(e.Value?.ToString(), out var parsed) && parsed;
        StoreEdited(row);
        MarkRowChanged(row);
    }

    private PayorDto? Original(PayorGridRow row) => _originalData.TryGetValue(row.Id, out var dto) ? dto : null;

    private void StoreEdited(PayorGridRow row) => _editedRows[row.Id] = row;

    private void MarkRowChanged(PayorGridRow row) => _changedRows.Add(row.Id);

    private string RowClass(PayorGridRow row, int index) => _changedRows.Contains(row.Id) ? "row-modified" : string.Empty;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusEditor)
            return;

        _focusEditor = false;
        try
        {
            if (_editingField == EditField.EffectiveDate)
            {
                // Clicking the date text opens the calendar immediately (parity item 13)
                if (_datePicker is not null)
                    await _datePicker.OpenAsync();
            }
            else if (_editingField != EditField.None)
            {
                await _editorRef.FocusAsync();
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            Logger.LogDebug(ex, "Could not focus the cell editor");
        }
    }

    #endregion

    #region Display helpers

    public static string FormatDate(DateTime? date) => date?.ToString(DateFormat, DateCulture) ?? string.Empty;

    private static string DisplayDate(DateTime? date) => date.HasValue ? FormatDate(date) : EmptyCell;

    private static string DisplayCategory(string category) => string.IsNullOrEmpty(category) ? EmptyCell : category;

    private static string StatusClass(PayorGridRow row) => row.IsActive ? "status-active" : "status-inactive";

    private static string StatusText(PayorGridRow row) => row.IsActive ? "Active" : "Inactive";

    #endregion

    #region Save All / Cancel

    /// <summary>Save All: sends every changed row; 'Saving...' and disabled while the call runs.</summary>
    private async Task SaveAllAsync()
    {
        if (!await AccessGuard.IsAuthorizedToModifyAsync())
        {
            _canModify = false;
            ShowToast(AccessDeniedText, Severity.Error);
            return;
        }

        var payors = _changedRows
            .Select(id => _editedRows.TryGetValue(id, out var row) ? row.ToUpdateRequest() : null)
            .OfType<UpdatePayorRequest>()
            .ToList();

        if (payors.Count == 0)
        {
            ShowToast("No changes to save.", Severity.Info);
            return;
        }

        EndEdit();
        _isSaving = true;
        StateHasChanged();

        try
        {
            var result = await PayorService.BatchUpdatePayorsAsync(payors);
            if (result.Success)
            {
                ShowToast($"Successfully saved {result.UpdatedCount} payor(s).", Severity.Success);
                ResetEditState();
                _isSaving = false;
                await ReloadGridAsync();
            }
            else
            {
                ShowToast(result.Message, Severity.Error);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogWarning(ex, "BatchUpdatePayors denied by the service guard");
            _canModify = false;
            ShowToast(AccessDeniedText, Severity.Error);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "BatchUpdatePayors failed");
            // Details stay in the log; the toast shows a fixed reason so WCF fault internals are not exposed
            ShowToast(SaveErrorText, Severity.Error);
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>Cancel: discards all edits without confirmation, reloads, info toast.</summary>
    private async Task CancelAllAsync()
    {
        ResetEditState();
        await ReloadGridAsync();
        ShowToast("Changes discarded.", Severity.Info);
    }

    private void ResetEditState()
    {
        EndEdit();
        _changedRows.Clear();
        _originalData.Clear();
        _editedRows.Clear();
    }

    private void ShowToast(string message, Severity severity)
    {
        Snackbar.Add(message, severity, options =>
        {
            options.VisibleStateDuration = ToastDurationMs;
            options.ShowTransitionDuration = ToastFadeMs;
            options.HideTransitionDuration = ToastFadeMs;
            options.ShowCloseIcon = false;
        });
    }

    #endregion
}
