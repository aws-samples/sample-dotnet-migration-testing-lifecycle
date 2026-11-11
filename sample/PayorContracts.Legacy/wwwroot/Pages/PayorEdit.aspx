<!DOCTYPE html>
<html>
<head>
    <title>Payor Batch Edit</title>
    <script src="https://code.jquery.com/jquery-3.7.1.min.js" integrity="sha384-1H217gwSVyLSIfaLxHbE7dRb3v4mYCKbpQvzx0cegeju1MVsGrX5xXxAvs/HgeFs" crossorigin="anonymous"></script>
    <link rel="stylesheet" href="https://cdn.datatables.net/1.13.7/css/jquery.dataTables.min.css" integrity="sha384-cya1SKeBBe5YTpHgx4WoICmzqkoHTxoGqjPySUq2nLksbJ/JiKXX2RmhyLz6jwST" crossorigin="anonymous" />
    <script src="https://cdn.datatables.net/1.13.7/js/jquery.dataTables.min.js" integrity="sha384-cjmdOgDzOE22dUheI5E6Gzd3upfmReW8N1y/4jwKQE50KYcvFKZJA9JxWgQOzqwQ" crossorigin="anonymous"></script>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background: #f5f5f5; }
        .page-header { background: #2c3e50; color: white; padding: 15px 20px; margin: -20px -20px 20px -20px; }
        .page-header h1 { margin: 0; font-size: 1.5em; }
        .action-bar { background: white; padding: 12px; border: 1px solid #ddd; border-radius: 4px; margin-bottom: 15px; display: flex; gap: 10px; align-items: center; }
        .btn { padding: 8px 20px; border: none; border-radius: 3px; cursor: pointer; font-weight: 600; }
        .btn-save { background: #3498db; color: white; }
        .btn-save:hover { background: #2980b9; }
        .btn-reset { background: #95a5a6; color: white; }
        .btn-reset:hover { background: #7f8c8d; }
        .summary { margin-left: auto; color: #7f8c8d; font-size: 0.9em; }
        .grid-container { background: white; padding: 15px; border: 1px solid #ddd; border-radius: 4px; }
        .error-panel { background: #fdf0f0; border: 1px solid #e74c3c; border-radius: 4px; padding: 12px; margin-bottom: 15px; display: none; }
        .error-panel h4 { color: #e74c3c; margin: 0 0 8px 0; }
        .error-panel ul { margin: 0; padding-left: 20px; color: #c0392b; }
        table.dataTable td input, table.dataTable td select { width: 100%; padding: 4px 6px; border: 1px solid #ccc; border-radius: 3px; box-sizing: border-box; font-size: 0.85em; }
        table.dataTable td input.invalid, table.dataTable td select.invalid { border: 2px solid #e74c3c; background: #fdf0f0; }
        table.dataTable td input:disabled, table.dataTable td select:disabled { background: #f0f0f0; color: #999; }
        .row-deleted td { opacity: 0.5; }
    </style>
</head>
<body>
    <div class="page-header">
        <h1>Payor Batch Edit</h1>
    </div>

    <div id="errorPanel" class="error-panel">
        <h4>Please fix the following errors:</h4>
        <ul id="errorList"></ul>
    </div>

    <div class="action-bar">
        <button class="btn btn-save" id="btnSave">Save All Changes</button>
        <button class="btn btn-reset" id="btnReset">Reset</button>
        <span class="summary" id="changeSummary"></span>
    </div>

    <div class="grid-container">
        <table id="editGrid" class="display" style="width:100%">
            <thead>
                <tr>
                    <th><input type="checkbox" id="checkAll" checked /></th>
                    <th>ID</th>
                    <th>Payor Name</th>
                    <th>Code</th>
                    <th>Status</th>
                    <th>Category</th>
                    <th>Effective Date</th>
                    <th>Max Value</th>
                </tr>
            </thead>
            <tbody></tbody>
        </table>
    </div>

    <script>
        var sampleData = [
            { id: 1, name: "Acme Health Plans", code: "ACM001", isActive: true, category: "Commercial", effectiveDate: "2023-01-15", maxValue: 500000 },
            { id: 2, name: "Bluebird Health Shield", code: "BBHS01", isActive: true, category: "Commercial", effectiveDate: "2022-06-01", maxValue: 750000 },
            { id: 3, name: "Mesa Senior Plan A", code: "MESA01", isActive: true, category: "Medicare", effectiveDate: "2021-01-01", maxValue: 1000000 },
            { id: 4, name: "Meadowlark State Plan", code: "MDW001", isActive: true, category: "Medicaid", effectiveDate: "2023-07-01", maxValue: 300000 },
            { id: 5, name: "Tamarack Federal Plan", code: "TMK001", isActive: false, category: "Tricare", effectiveDate: "2020-03-15", maxValue: 200000 },
            { id: 6, name: "Citrine Valley Healthcare", code: "CTR001", isActive: true, category: "Commercial", effectiveDate: "2024-01-01", maxValue: 600000 }
        ];

        $(document).ready(function () {
            var table = $('#editGrid').DataTable({
                data: sampleData,
                paging: true,
                pageLength: 5,
                ordering: false,
                searching: false,
                columns: [
                    {
                        data: null,
                        render: function (data, type, row) {
                            return '<input type="checkbox" class="row-check" data-id="' + row.id + '" checked />';
                        }
                    },
                    { data: 'id' },
                    {
                        data: 'name',
                        render: function (data, type, row) {
                            return '<input type="text" class="edit-name" data-id="' + row.id + '" value="' + data + '" />';
                        }
                    },
                    {
                        data: 'code',
                        render: function (data, type, row) {
                            return '<input type="text" class="edit-code" data-id="' + row.id + '" value="' + data + '" />';
                        }
                    },
                    {
                        data: 'isActive',
                        render: function (data, type, row) {
                            return '<select class="edit-status" data-id="' + row.id + '">' +
                                '<option value="true"' + (data ? ' selected' : '') + '>Active</option>' +
                                '<option value="false"' + (!data ? ' selected' : '') + '>Inactive</option></select>';
                        }
                    },
                    {
                        data: 'category',
                        render: function (data, type, row) {
                            var opts = ['Commercial', 'Medicare', 'Medicaid', 'Tricare'];
                            var html = '<select class="edit-category" data-id="' + row.id + '"><option value="">-- Select --</option>';
                            opts.forEach(function (o) {
                                html += '<option value="' + o + '"' + (data === o ? ' selected' : '') + '>' + o + '</option>';
                            });
                            return html + '</select>';
                        }
                    },
                    {
                        data: 'effectiveDate',
                        render: function (data, type, row) {
                            return '<input type="date" class="edit-date" data-id="' + row.id + '" value="' + data + '" />';
                        }
                    },
                    {
                        data: 'maxValue',
                        render: function (data, type, row) {
                            return '<input type="number" class="edit-value" data-id="' + row.id + '" value="' + data + '" min="0" />';
                        }
                    }
                ]
            });

            // Toggle all checkboxes
            $('#checkAll').on('change', function () {
                var checked = $(this).is(':checked');
                $('.row-check').prop('checked', checked).trigger('change');
            });

            // Disable row inputs when unchecked (mark for deletion)
            $('#editGrid').on('change', '.row-check', function () {
                var row = $(this).closest('tr');
                var isChecked = $(this).is(':checked');
                row.find('input:not(.row-check), select').prop('disabled', !isChecked);
                row.toggleClass('row-deleted', !isChecked);
                updateSummary();
            });

            // Save All — batch validate and submit
            $('#btnSave').on('click', function () {
                var errors = [];
                $('.invalid').removeClass('invalid');
                $('#errorPanel').hide();

                // Validate each included row
                $('.row-check:checked').each(function () {
                    var id = $(this).data('id');
                    var row = $(this).closest('tr');

                    var name = row.find('.edit-name').val().trim();
                    if (!name || name.length < 3) {
                        errors.push('Row ' + id + ': Payor Name is required (min 3 characters).');
                        row.find('.edit-name').addClass('invalid');
                    }

                    var code = row.find('.edit-code').val().trim();
                    if (!code || code.length < 3) {
                        errors.push('Row ' + id + ': Code is required (min 3 characters).');
                        row.find('.edit-code').addClass('invalid');
                    }

                    var category = row.find('.edit-category').val();
                    if (!category) {
                        errors.push('Row ' + id + ' (' + name + '): Category is required.');
                        row.find('.edit-category').addClass('invalid');
                    }

                    var date = row.find('.edit-date').val();
                    if (!date) {
                        errors.push('Row ' + id + ' (' + name + '): Effective Date is required.');
                        row.find('.edit-date').addClass('invalid');
                    }

                    var value = parseFloat(row.find('.edit-value').val());
                    if (!value || value <= 0) {
                        errors.push('Row ' + id + ' (' + name + '): Max Value must be greater than 0.');
                        row.find('.edit-value').addClass('invalid');
                    }
                });

                if (errors.length > 0) {
                    // Build the list with .text() so user-entered values (Payor Name) are
                    // never parsed as HTML. Do not switch this back to string concatenation.
                    var $list = $('#errorList').empty();
                    errors.forEach(function (e) { $('<li>').text(e).appendTo($list); });
                    $('#errorPanel').show();
                    alert('Validation failed — ' + errors.length + ' error(s). Please fix and try again.');
                    return;
                }

                // Collect data for submission
                var toSave = [];
                var toDelete = [];
                $('.row-check').each(function () {
                    var id = $(this).data('id');
                    if ($(this).is(':checked')) {
                        var row = $(this).closest('tr');
                        toSave.push({
                            id: id,
                            name: row.find('.edit-name').val(),
                            code: row.find('.edit-code').val(),
                            isActive: row.find('.edit-status').val() === 'true',
                            category: row.find('.edit-category').val(),
                            effectiveDate: row.find('.edit-date').val(),
                            maxValue: parseFloat(row.find('.edit-value').val())
                        });
                    } else {
                        toDelete.push(id);
                    }
                });

                $('#errorPanel').hide();
                alert('Batch saved: ' + toSave.length + ' updated, ' + toDelete.length + ' deleted.');
            });

            // Reset
            $('#btnReset').on('click', function () {
                table.clear().rows.add(sampleData).draw();
                $('#errorPanel').hide();
                $('.invalid').removeClass('invalid');
                updateSummary();
            });

            function updateSummary() {
                var total = $('.row-check').length;
                var deleted = $('.row-check:not(:checked)').length;
                var included = total - deleted;
                var text = deleted > 0 ? included + ' to save, ' + deleted + ' to delete' : included + ' records';
                $('#changeSummary').text(text);
            }

            updateSummary();
        });
    </script>
</body>
</html>
