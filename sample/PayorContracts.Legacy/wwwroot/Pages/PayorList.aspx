<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="PayorList.aspx.cs" Inherits="PayorContracts.Legacy.Pages.PayorList" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Payor Information</title>
    <!-- jQuery -->
    <script src="https://code.jquery.com/jquery-3.7.1.min.js" integrity="sha384-1H217gwSVyLSIfaLxHbE7dRb3v4mYCKbpQvzx0cegeju1MVsGrX5xXxAvs/HgeFs" crossorigin="anonymous"></script>
    <!-- jQuery UI (required by jqGrid) -->
    <link rel="stylesheet" href="https://code.jquery.com/ui/1.13.2/themes/base/jquery-ui.min.css" integrity="sha384-J3tLcWkdGTGEaRTYfKrKVaK5EGVBuxR9rg5ZzQFWRuQD+0hZABemSLVXimw8Nrb9" crossorigin="anonymous" />
    <script src="https://code.jquery.com/ui/1.13.2/jquery-ui.min.js" integrity="sha384-4D3G3GikQs6hLlLZGdz5wLFzuqE9v4yVGAcOH86y23JqBDPzj9viv0EqyfIa6YUL" crossorigin="anonymous"></script>
    <!-- free-jqGrid -->
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/free-jqgrid@4.15.5/css/ui.jqgrid.min.css" integrity="sha384-PD4jmreZU01agadYECQOKKjYA0wjIklb4xDi9mgam9frrgrdn39UbCi04GF2kjQM" crossorigin="anonymous" />
    <script src="https://cdn.jsdelivr.net/npm/free-jqgrid@4.15.5/js/jquery.jqgrid.min.js" integrity="sha384-HuS/rN5CMf0Ps+5qia/g0J8K/Jr83K8G1OAgoptn1VLNMFqZPJv/KjCrZRQV94L5" crossorigin="anonymous"></script>
    <!-- Flatpickr for date picker -->
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/flatpickr@4.6.13/dist/flatpickr.min.css" integrity="sha384-RkASv+6KfBMW9eknReJIJ6b3UnjKOKC5bOUaNgIY778NFbQ8MtWq9Lr/khUgqtTt" crossorigin="anonymous" />
    <script src="https://cdn.jsdelivr.net/npm/flatpickr@4.6.13/dist/flatpickr.min.js" integrity="sha384-5JqMv4L/Xa0hfvtF06qboNdhvuYXUku9ZrhZh3bSk8VXF0A/RuSLHpLsSV9Zqhl6" crossorigin="anonymous"></script>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background: #f5f5f5; }
        .page-header { background: #2c3e50; color: white; padding: 15px 20px; margin: -20px -20px 20px -20px; }
        .page-header h1 { margin: 0; font-size: 1.5em; }
        .filter-panel { background: white; padding: 15px; border: 1px solid #ddd; border-radius: 4px; margin-bottom: 15px; }
        .filter-panel label { display: inline-block; margin-right: 5px; font-weight: 600; font-size: 0.9em; }
        .filter-panel input, .filter-panel select { padding: 6px 10px; border: 1px solid #ccc; border-radius: 3px; margin-right: 15px; }
        .filter-panel button { padding: 7px 20px; background: #3498db; color: white; border: none; border-radius: 3px; cursor: pointer; font-weight: 600; }
        .filter-panel button:hover { background: #2980b9; }
        .filter-panel .btn-clear { background: #95a5a6; }
        .filter-panel .btn-clear:hover { background: #7f8c8d; }
        .grid-container { background: white; padding: 15px; border: 1px solid #ddd; border-radius: 4px; }
        /* Click-to-edit cell styles */
        .cell-display { cursor: pointer; padding: 4px 6px; min-height: 24px; display: inline-block; border: 1px solid transparent; border-radius: 3px; width: 100%; box-sizing: border-box; }
        .cell-display:hover { background: #eaf6ff; border-color: #bde0fe; }
        .edit-input { padding: 4px 6px; border: 1px solid #3498db; border-radius: 3px; width: 95%; font-size: 0.9em; outline: none; box-sizing: border-box; }
        .edit-select { padding: 4px 6px; border: 1px solid #3498db; border-radius: 3px; font-size: 0.9em; outline: none; width: 100%; box-sizing: border-box; }
        .edit-date { padding: 4px 6px; border: 1px solid #3498db; border-radius: 3px; width: 100%; font-size: 0.9em; outline: none; box-sizing: border-box; }
        .status-active { color: #27ae60; font-weight: 600; }
        .status-inactive { color: #e74c3c; font-weight: 600; }
        .row-modified { background-color: #fef9e7 !important; }
        /* Action bar */
        .action-bar { background: white; padding: 12px 15px; border: 1px solid #ddd; border-radius: 4px; margin-top: 15px; display: flex; justify-content: flex-end; gap: 10px; align-items: center; }
        .action-bar .change-count { color: #7f8c8d; font-size: 0.9em; margin-right: auto; }
        .btn-save-all { padding: 8px 24px; background: #27ae60; color: white; border: none; border-radius: 3px; cursor: pointer; font-weight: 600; font-size: 0.95em; }
        .btn-save-all:hover { background: #1e8449; }
        .btn-save-all:disabled { background: #bdc3c7; cursor: not-allowed; }
        .btn-cancel-all { padding: 8px 24px; background: #e74c3c; color: white; border: none; border-radius: 3px; cursor: pointer; font-weight: 600; font-size: 0.95em; }
        .btn-cancel-all:hover { background: #c0392b; }
        .btn-cancel-all:disabled { background: #bdc3c7; cursor: not-allowed; }
        /* Toast notification */
        .toast { position: fixed; top: 20px; right: 20px; padding: 12px 20px; border-radius: 4px; color: white; font-weight: 600; z-index: 9999; display: none; box-shadow: 0 2px 8px rgba(0,0,0,0.2); }
        .toast-success { background: #27ae60; }
        .toast-error { background: #e74c3c; }
        .toast-info { background: #3498db; }
        /* jqGrid overrides for consistent look */
        .ui-jqgrid { border: 1px solid #ddd; }
        .ui-jqgrid .ui-jqgrid-htable th { background: #f8f9fa; font-weight: 600; font-size: 0.9em; }
        .ui-jqgrid tr.jqgrow td { padding: 6px 8px; font-size: 0.9em; }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <div class="page-header">
            <h1>Payor Information Management</h1>
        </div>

        <!-- Toast notification -->
        <div id="toast" class="toast"></div>

        <!-- Filter Panel -->
        <div class="filter-panel">
            <label for="txtNameFilter">Payor Name:</label>
            <input type="text" id="txtNameFilter" placeholder="Search by name..." />

            <label for="txtCodeFilter">Code:</label>
            <input type="text" id="txtCodeFilter" placeholder="Search by code..." />

            <label for="ddlActiveFilter">Status:</label>
            <select id="ddlActiveFilter">
                <option value="">All</option>
                <option value="true">Active</option>
                <option value="false">Inactive</option>
            </select>

            <button id="btnSearch" type="button">Search</button>
            <button id="btnClear" type="button" class="btn-clear">Clear</button>
        </div>

        <!-- jqGrid Container -->
        <div class="grid-container">
            <table id="payorGrid"></table>
            <div id="payorPager"></div>
        </div>

        <!-- Batch Save / Cancel Action Bar -->
        <div class="action-bar">
            <span class="change-count" id="changeCount">No unsaved changes</span>
            <button type="button" class="btn-cancel-all" id="btnCancelAll" disabled>Cancel</button>
            <button type="button" class="btn-save-all" id="btnSaveAll" disabled>Save All</button>
        </div>
    </form>

    <script type="text/javascript">
        $(document).ready(function () {
            var changedRows = {};
            var originalData = {};
            var categories = ['Commercial', 'Medicare', 'Medicaid', 'Tricare'];

            function showToast(message, type) {
                var $toast = $('#toast');
                $toast.removeClass('toast-success toast-error toast-info').addClass('toast-' + type).text(message).fadeIn(300);
                setTimeout(function () { $toast.fadeOut(300); }, 3000);
            }

            function updateActionBar() {
                var count = Object.keys(changedRows).length;
                if (count > 0) {
                    $('#changeCount').text(count + ' unsaved change(s)');
                    $('#btnSaveAll').prop('disabled', false).text('Save All (' + count + ')');
                    $('#btnCancelAll').prop('disabled', false);
                } else {
                    $('#changeCount').text('No unsaved changes');
                    $('#btnSaveAll').prop('disabled', true).text('Save All');
                    $('#btnCancelAll').prop('disabled', true);
                }
            }

            function markRowChanged(id) {
                changedRows[id] = true;
                $('#' + id).addClass('row-modified');
                updateActionBar();
            }

            function formatDate(dateStr) {
                if (!dateStr) return '';
                var d = new Date(dateStr);
                if (isNaN(d.getTime())) return '';
                return (d.getMonth() + 1).toString().padStart(2, '0') + '/' +
                       d.getDate().toString().padStart(2, '0') + '/' +
                       d.getFullYear();
            }

            function escapeHtml(str) {
                if (!str) return '';
                return $('<span>').text(str).html();
            }

            // Custom formatters for click-to-edit cells
            function nameFormatter(cellValue, options, rowObject) {
                return '<span class="cell-display cell-name">' + escapeHtml(cellValue) + '</span>';
            }

            function codeFormatter(cellValue, options, rowObject) {
                return '<span class="cell-display cell-code">' + escapeHtml(cellValue) + '</span>';
            }

            function categoryFormatter(cellValue, options, rowObject) {
                return '<span class="cell-display cell-category">' + escapeHtml(cellValue || '—') + '</span>';
            }

            function dateFormatter(cellValue, options, rowObject) {
                return '<span class="cell-display cell-date">' + (formatDate(cellValue) || '—') + '</span>';
            }

            function statusFormatter(cellValue, options, rowObject) {
                var checked = (cellValue === true || cellValue === 'true') ? ' checked' : '';
                var label = (cellValue === true || cellValue === 'true')
                    ? '<span class="status-active">Active</span>'
                    : '<span class="status-inactive">Inactive</span>';
                return '<label style="cursor:pointer"><input type="checkbox" class="edit-status"' + checked + ' /> ' + label + '</label>';
            }

            // Initialize jqGrid
            $('#payorGrid').jqGrid({
                url: '/WebServices/PayorService.asmx/GetPayors',
                mtype: 'POST',
                datatype: 'json',
                ajaxGridOptions: { contentType: 'application/json; charset=utf-8' },
                serializeGridData: function (postData) {
                    return JSON.stringify({
                        nameFilter: $('#txtNameFilter').val() || '',
                        codeFilter: $('#txtCodeFilter').val() || '',
                        activeOnly: $('#ddlActiveFilter').val() === '' ? null : ($('#ddlActiveFilter').val() === 'true'),
                        sortColumn: postData.sidx || 'id',
                        sortDirection: postData.sord || 'asc',
                        page: postData.page || 1,
                        pageSize: postData.rows || 10
                    });
                },
                jsonReader: {
                    root: function (obj) {
                        var data = obj.d || obj;
                        // Store original data
                        if (data.data) {
                            data.data.forEach(function (row) {
                                if (!originalData[row.id]) {
                                    originalData[row.id] = JSON.parse(JSON.stringify(row));
                                }
                            });
                        }
                        return data.data || [];
                    },
                    page: function (obj) { var d = obj.d || obj; return d.currentPage || 1; },
                    total: function (obj) { var d = obj.d || obj; return d.totalPages || 1; },
                    records: function (obj) { var d = obj.d || obj; return d.totalRecords || 0; },
                    repeatitems: false,
                    id: 'id'
                },
                colModel: [
                    { name: 'id', label: 'ID', width: 50, sortable: true, align: 'center' },
                    { name: 'name', label: 'Payor Name', width: 200, sortable: true, formatter: nameFormatter },
                    { name: 'code', label: 'Code', width: 100, sortable: true, formatter: codeFormatter },
                    { name: 'category', label: 'Category', width: 120, sortable: true, formatter: categoryFormatter },
                    { name: 'effectiveDate', label: 'Effective Date', width: 130, sortable: true, formatter: dateFormatter },
                    { name: 'isActive', label: 'Status', width: 100, sortable: true, formatter: statusFormatter }
                ],
                pager: '#payorPager',
                rowNum: 10,
                rowList: [5, 10, 15, 25],
                sortname: 'id',
                sortorder: 'asc',
                viewrecords: true,
                height: 'auto',
                autowidth: true,
                gridComplete: function () {
                    // Re-highlight changed rows after grid refresh
                    var ids = Object.keys(changedRows);
                    for (var i = 0; i < ids.length; i++) {
                        $('#' + ids[i]).addClass('row-modified');
                    }
                }
            });

            // --- Click-to-edit: Name ---
            $('#payorGrid').on('click', '.cell-name', function (e) {
                e.stopPropagation();
                var $td = $(this).closest('td');
                var rowId = $(this).closest('tr').attr('id');
                var rowData = $('#payorGrid').jqGrid('getRowData', rowId);
                // Get actual data from local data
                var localData = $('#payorGrid').jqGrid('getLocalRow', rowId) || rowData;
                var currentVal = originalData[rowId] ? (changedRows[rowId] ? getEditedValue(rowId, 'name') : originalData[rowId].name) : '';
                // Read from the cell display text
                currentVal = $(this).text();

                $td.html('<input type="text" class="edit-input edit-name-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" />');
                $td.find('.edit-name-input').focus().select();
            });

            $('#payorGrid').on('blur', '.edit-name-input', function () {
                var rowId = $(this).data('rowid');
                var $td = $(this).closest('td');
                var newVal = $(this).val();

                $td.html('<span class="cell-display cell-name">' + escapeHtml(newVal) + '</span>');
                storeEditedValue(rowId, 'name', newVal);

                if (originalData[rowId] && newVal !== originalData[rowId].name) {
                    markRowChanged(rowId);
                }
            });

            $('#payorGrid').on('keydown', '.edit-name-input', function (e) {
                if (e.which === 13) { $(this).blur(); }
                if (e.which === 27) {
                    var rowId = $(this).data('rowid');
                    var $td = $(this).closest('td');
                    var origVal = originalData[rowId] ? originalData[rowId].name : '';
                    $td.html('<span class="cell-display cell-name">' + escapeHtml(origVal) + '</span>');
                }
            });

            // --- Click-to-edit: Code ---
            $('#payorGrid').on('click', '.cell-code', function (e) {
                e.stopPropagation();
                var $td = $(this).closest('td');
                var rowId = $(this).closest('tr').attr('id');
                var currentVal = $(this).text();

                $td.html('<input type="text" class="edit-input edit-code-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" />');
                $td.find('.edit-code-input').focus().select();
            });

            $('#payorGrid').on('blur', '.edit-code-input', function () {
                var rowId = $(this).data('rowid');
                var $td = $(this).closest('td');
                var newVal = $(this).val();

                $td.html('<span class="cell-display cell-code">' + escapeHtml(newVal) + '</span>');
                storeEditedValue(rowId, 'code', newVal);

                if (originalData[rowId] && newVal !== originalData[rowId].code) {
                    markRowChanged(rowId);
                }
            });

            $('#payorGrid').on('keydown', '.edit-code-input', function (e) {
                if (e.which === 13) { $(this).blur(); }
                if (e.which === 27) {
                    var rowId = $(this).data('rowid');
                    var $td = $(this).closest('td');
                    var origVal = originalData[rowId] ? originalData[rowId].code : '';
                    $td.html('<span class="cell-display cell-code">' + escapeHtml(origVal) + '</span>');
                }
            });

            // --- Click-to-edit: Category (dropdown) ---
            $('#payorGrid').on('click', '.cell-category', function (e) {
                e.stopPropagation();
                var $td = $(this).closest('td');
                var rowId = $(this).closest('tr').attr('id');
                var currentVal = $(this).text().trim();
                if (currentVal === '—') currentVal = '';

                var html = '<select class="edit-select edit-category-input" data-rowid="' + rowId + '">';
                for (var i = 0; i < categories.length; i++) {
                    var sel = (categories[i] === currentVal) ? ' selected' : '';
                    html += '<option value="' + categories[i] + '"' + sel + '>' + categories[i] + '</option>';
                }
                html += '</select>';
                $td.html(html);
                $td.find('.edit-category-input').focus();
            });

            $('#payorGrid').on('change', '.edit-category-input', function () {
                $(this).blur();
            });

            $('#payorGrid').on('blur', '.edit-category-input', function () {
                var rowId = $(this).data('rowid');
                var $td = $(this).closest('td');
                var newVal = $(this).val();

                $td.html('<span class="cell-display cell-category">' + escapeHtml(newVal) + '</span>');
                storeEditedValue(rowId, 'category', newVal);

                if (originalData[rowId] && newVal !== originalData[rowId].category) {
                    markRowChanged(rowId);
                }
            });

            // --- Click-to-edit: Effective Date (flatpickr) ---
            $('#payorGrid').on('click', '.cell-date', function (e) {
                e.stopPropagation();
                var $td = $(this).closest('td');
                var rowId = $(this).closest('tr').attr('id');
                var currentVal = $(this).text().trim();
                if (currentVal === '—') currentVal = '';

                $td.html('<input type="text" class="edit-date edit-date-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" placeholder="MM/DD/YYYY" />');
                var $input = $td.find('.edit-date-input');

                flatpickr($input.get(0), {
                    dateFormat: 'm/d/Y',
                    allowInput: true,
                    defaultDate: currentVal || null,
                    onClose: function (selectedDates, dateStr) {
                        var rid = $input.data('rowid');
                        var $c = $input.closest('td');

                        $c.html('<span class="cell-display cell-date">' + escapeHtml(dateStr || '—') + '</span>');
                        storeEditedValue(rid, 'effectiveDate', dateStr || null);

                        if (originalData[rid] && dateStr !== formatDate(originalData[rid].effectiveDate)) {
                            markRowChanged(rid);
                        }
                    }
                });
                $input.get(0)._flatpickr.open();
            });

            // --- Status checkbox (always editable) ---
            $('#payorGrid').on('change', '.edit-status', function () {
                var rowId = $(this).closest('tr').attr('id');
                var isActive = $(this).is(':checked');

                $(this).siblings('span').html(
                    isActive ? '<span class="status-active">Active</span>' : '<span class="status-inactive">Inactive</span>'
                );
                storeEditedValue(rowId, 'isActive', isActive);
                markRowChanged(rowId);
            });

            // --- Store edited values ---
            var editedValues = {};

            function storeEditedValue(rowId, field, value) {
                if (!editedValues[rowId]) {
                    editedValues[rowId] = JSON.parse(JSON.stringify(originalData[rowId] || {}));
                }
                editedValues[rowId][field] = value;
            }

            function getEditedValue(rowId, field) {
                if (editedValues[rowId] && editedValues[rowId][field] !== undefined) {
                    return editedValues[rowId][field];
                }
                return originalData[rowId] ? originalData[rowId][field] : '';
            }

            // --- Save All ---
            $('#btnSaveAll').on('click', function () {
                var payors = [];
                var ids = Object.keys(changedRows);

                for (var i = 0; i < ids.length; i++) {
                    var id = ids[i];
                    var data = editedValues[id] || originalData[id];
                    if (data) {
                        payors.push({
                            id: parseInt(id),
                            name: data.name,
                            code: data.code,
                            category: data.category,
                            effectiveDate: data.effectiveDate || null,
                            isActive: data.isActive
                        });
                    }
                }

                if (payors.length === 0) {
                    showToast('No changes to save.', 'info');
                    return;
                }

                $('#btnSaveAll').prop('disabled', true).text('Saving...');

                $.ajax({
                    url: '/WebServices/PayorService.asmx/BatchUpdatePayors',
                    type: 'POST',
                    contentType: 'application/json; charset=utf-8',
                    dataType: 'json',
                    data: JSON.stringify({ payors: payors }),
                    success: function (response) {
                        var result = response.d || response;
                        if (result.success || result.Success) {
                            showToast('Successfully saved ' + (result.updatedCount || result.UpdatedCount) + ' payor(s).', 'success');
                            changedRows = {};
                            originalData = {};
                            editedValues = {};
                            updateActionBar();
                            $('#payorGrid').trigger('reloadGrid');
                        } else {
                            showToast((result.message || result.Message), 'error');
                        }
                        $('#btnSaveAll').prop('disabled', false);
                        updateActionBar();
                    },
                    error: function (xhr, status, error) {
                        showToast('Error saving changes: ' + error, 'error');
                        $('#btnSaveAll').prop('disabled', false);
                        updateActionBar();
                    }
                });
            });

            // --- Cancel All ---
            $('#btnCancelAll').on('click', function () {
                changedRows = {};
                originalData = {};
                editedValues = {};
                updateActionBar();
                $('#payorGrid').trigger('reloadGrid');
                showToast('Changes discarded.', 'info');
            });

            // --- Filter buttons ---
            $('#btnSearch').on('click', function () {
                $('#payorGrid').jqGrid('setGridParam', { page: 1 }).trigger('reloadGrid');
            });

            $('#btnClear').on('click', function () {
                $('#txtNameFilter').val('');
                $('#txtCodeFilter').val('');
                $('#ddlActiveFilter').val('');
                $('#payorGrid').jqGrid('setGridParam', { page: 1 }).trigger('reloadGrid');
            });

            $('#txtNameFilter, #txtCodeFilter').on('keypress', function (e) {
                if (e.which === 13) {
                    e.preventDefault();
                    $('#payorGrid').jqGrid('setGridParam', { page: 1 }).trigger('reloadGrid');
                }
            });
        });
    </script>
</body>
</html>
