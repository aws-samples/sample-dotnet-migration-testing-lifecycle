<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head>
    <title>Contract Hierarchy</title>
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
        .grid-section { background: white; padding: 15px; border: 1px solid #ddd; border-radius: 4px; margin-bottom: 15px; }
        .section-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
        .section-header h2 { margin: 0; font-size: 1.15em; color: #2c3e50; }
        .btn-add { padding: 7px 16px; background: #3498db; color: white; border: none; border-radius: 3px; cursor: pointer; font-weight: 600; font-size: 0.9em; }
        .btn-add:hover { background: #2980b9; }
        /* Click-to-edit cell styles */
        .cell-display { cursor: pointer; padding: 4px 6px; min-height: 24px; display: inline-block; border: 1px solid transparent; border-radius: 3px; width: 100%; box-sizing: border-box; }
        .cell-display:hover { background: #eaf6ff; border-color: #bde0fe; }
        .edit-input { padding: 4px 6px; border: 1px solid #3498db; border-radius: 3px; width: 95%; font-size: 0.9em; outline: none; box-sizing: border-box; }
        .edit-select { padding: 4px 6px; border: 1px solid #3498db; border-radius: 3px; font-size: 0.9em; outline: none; width: 100%; box-sizing: border-box; }
        .edit-date { padding: 4px 6px; border: 1px solid #3498db; border-radius: 3px; width: 100%; font-size: 0.9em; outline: none; box-sizing: border-box; }
        .status-active { color: #27ae60; font-weight: 600; }
        .status-inactive { color: #e74c3c; font-weight: 600; }
        .status-pending { color: #f39c12; font-weight: 600; }
        .status-terminated { color: #e74c3c; font-weight: 600; }
        .row-modified { background-color: #fef9e7 !important; }
        /* Action bar */
        .action-bar { background: #f8f9fa; padding: 8px 12px; border: 1px solid #ddd; border-radius: 3px; margin-top: 8px; display: flex; justify-content: flex-end; gap: 8px; align-items: center; }
        .action-bar .change-count { color: #7f8c8d; font-size: 0.85em; margin-right: auto; }
        .btn-save { padding: 6px 16px; background: #27ae60; color: white; border: none; border-radius: 3px; cursor: pointer; font-weight: 600; font-size: 0.85em; }
        .btn-save:hover { background: #1e8449; }
        .btn-save:disabled { background: #bdc3c7; cursor: not-allowed; }
        .btn-cancel { padding: 6px 16px; background: #e74c3c; color: white; border: none; border-radius: 3px; cursor: pointer; font-weight: 600; font-size: 0.85em; }
        .btn-cancel:hover { background: #c0392b; }
        .btn-cancel:disabled { background: #bdc3c7; cursor: not-allowed; }
        /* Toast notification */
        .toast { position: fixed; top: 20px; right: 20px; padding: 12px 20px; border-radius: 4px; color: white; font-weight: 600; z-index: 9999; display: none; box-shadow: 0 2px 8px rgba(0,0,0,0.2); }
        .toast-success { background: #27ae60; }
        .toast-error { background: #e74c3c; }
        .toast-info { background: #3498db; }
        /* jqGrid overrides */
        .ui-jqgrid { border: 1px solid #ddd; }
        .ui-jqgrid .ui-jqgrid-htable th { background: #f8f9fa; font-weight: 600; font-size: 0.9em; }
        .ui-jqgrid tr.jqgrow td { padding: 6px 8px; font-size: 0.9em; }
        /* Subgrid styling */
        .ui-jqgrid .ui-sgcollapsed span { cursor: pointer; }
        .subgrid-header { display: flex; align-items: center; justify-content: space-between; padding: 8px 12px; background: #f0f4f8; border-bottom: 1px solid #ddd; }
        .subgrid-header h3 { margin: 0; font-size: 1em; color: #2c3e50; }
        .subgrid-container { padding: 8px; }
        /* Dialog styles */
        .dialog-field { margin-bottom: 12px; }
        .dialog-field label { display: block; font-weight: 600; font-size: 0.9em; margin-bottom: 4px; color: #2c3e50; }
        .dialog-field input, .dialog-field select { width: 100%; padding: 8px 10px; border: 1px solid #ccc; border-radius: 3px; font-size: 0.9em; box-sizing: border-box; }
        .dialog-field .validation-msg { color: #e74c3c; font-size: 0.8em; margin-top: 3px; display: none; }
    </style>
</head>
<body>
    <div class="page-header">
        <h1>Contract Hierarchy Management</h1>
    </div>

    <!-- Toast notification -->
    <div id="toast" class="toast"></div>

    <!-- Level 1: Contracts -->
    <div class="grid-section" id="contractSection">
        <div class="section-header">
            <h2>Contracts</h2>
            <button type="button" class="btn-add" id="btnAddContract">+ Add Contract</button>
        </div>
        <table id="contractGrid"></table>
        <div class="action-bar" id="contractActionBar">
            <span class="change-count" id="contractChangeCount">No unsaved changes</span>
            <button type="button" class="btn-cancel" id="btnCancelContracts" disabled>Cancel</button>
            <button type="button" class="btn-save" id="btnSaveContracts" disabled>Save All</button>
        </div>
    </div>

    <!-- Add Contract Dialog -->
    <div id="dlgAddContract" title="Add New Contract" style="display:none;">
        <div class="dialog-field">
            <label for="dlgContractName">Contract Name *</label>
            <input type="text" id="dlgContractName" placeholder="Min 3 characters" />
            <div class="validation-msg" id="valContractName">Name must be at least 3 characters.</div>
        </div>
        <div class="dialog-field">
            <label for="dlgContractType">Contract Type *</label>
            <select id="dlgContractType">
                <option value="Commercial">Commercial</option>
                <option value="Government">Government</option>
                <option value="Medicare">Medicare</option>
                <option value="Medicaid">Medicaid</option>
            </select>
        </div>
        <div class="dialog-field">
            <label for="dlgContractStart">Start Date</label>
            <input type="text" id="dlgContractStart" placeholder="MM/DD/YYYY" />
        </div>
        <div class="dialog-field">
            <label for="dlgContractEnd">End Date</label>
            <input type="text" id="dlgContractEnd" placeholder="MM/DD/YYYY" />
            <div class="validation-msg" id="valContractDates">Start Date must be before End Date.</div>
        </div>
        <div class="dialog-field">
            <label><input type="checkbox" id="dlgContractActive" checked /> Active</label>
        </div>
    </div>

    <!-- Add Plan Dialog -->
    <div id="dlgAddPlan" title="Add New Plan" style="display:none;">
        <div class="dialog-field">
            <label for="dlgPlanName">Plan Name *</label>
            <input type="text" id="dlgPlanName" placeholder="Min 3 characters" />
            <div class="validation-msg" id="valPlanName">Name must be at least 3 characters.</div>
        </div>
        <div class="dialog-field">
            <label for="dlgPlanCode">Plan Code *</label>
            <input type="text" id="dlgPlanCode" placeholder="Plan code" />
            <div class="validation-msg" id="valPlanCode">Plan code is required.</div>
        </div>
        <div class="dialog-field">
            <label for="dlgPlanEffective">Effective Date</label>
            <input type="text" id="dlgPlanEffective" placeholder="MM/DD/YYYY" />
        </div>
        <div class="dialog-field">
            <label for="dlgPlanTerm">Term Date</label>
            <input type="text" id="dlgPlanTerm" placeholder="MM/DD/YYYY" />
            <div class="validation-msg" id="valPlanDates">Effective Date must be before Term Date.</div>
        </div>
        <div class="dialog-field">
            <label for="dlgPlanStatus">Status</label>
            <select id="dlgPlanStatus">
                <option value="Active">Active</option>
                <option value="Pending">Pending</option>
                <option value="Terminated">Terminated</option>
            </select>
        </div>
    </div>

    <!-- Add Benefit Dialog -->
    <div id="dlgAddBenefit" title="Add New Benefit" style="display:none;">
        <div class="dialog-field">
            <label for="dlgBenefitName">Benefit Name *</label>
            <input type="text" id="dlgBenefitName" placeholder="Min 3 characters" />
            <div class="validation-msg" id="valBenefitName">Name must be at least 3 characters.</div>
        </div>
        <div class="dialog-field">
            <label for="dlgBenefitCoverage">Coverage Type *</label>
            <select id="dlgBenefitCoverage">
                <option value="Medical">Medical</option>
                <option value="Dental">Dental</option>
                <option value="Vision">Vision</option>
                <option value="Pharmacy">Pharmacy</option>
                <option value="Behavioral">Behavioral</option>
            </select>
        </div>
        <div class="dialog-field">
            <label for="dlgBenefitCopay">Copay Amount ($) *</label>
            <input type="number" id="dlgBenefitCopay" min="0" step="0.01" value="0" />
            <div class="validation-msg" id="valBenefitCopay">Copay must be >= 0.</div>
        </div>
        <div class="dialog-field">
            <label for="dlgBenefitStart">Start Date</label>
            <input type="text" id="dlgBenefitStart" placeholder="MM/DD/YYYY" />
        </div>
        <div class="dialog-field">
            <label for="dlgBenefitEnd">End Date</label>
            <input type="text" id="dlgBenefitEnd" placeholder="MM/DD/YYYY" />
            <div class="validation-msg" id="valBenefitDates">Start Date must be before End Date.</div>
        </div>
    </div>

    <script type="text/javascript">
    $(document).ready(function () {
        // ===== State =====
        var contractChanges = {};
        var contractOriginal = {};
        var contractEdited = {};

        // Per-subgrid state keyed by contractId or planId
        var planChangesByContract = {};   // { contractId: { planId: true } }
        var planOriginalByContract = {};  // { contractId: { planId: {...} } }
        var planEditedByContract = {};    // { contractId: { planId: {...} } }

        var benefitChangesByPlan = {};    // { planId: { benefitId: true } }
        var benefitOriginalByPlan = {};   // { planId: { benefitId: {...} } }
        var benefitEditedByPlan = {};     // { planId: { benefitId: {...} } }

        // Track which subgrid dialog is for
        var currentAddPlanContractId = null;
        var currentAddBenefitPlanId = null;
        var currentAddBenefitContractId = null;

        var contractTypes = ['Commercial', 'Government', 'Medicare', 'Medicaid'];
        var planStatuses = ['Active', 'Pending', 'Terminated'];
        var coverageTypes = ['Medical', 'Dental', 'Vision', 'Pharmacy', 'Behavioral'];

        // ===== Utilities =====
        function showToast(message, type) {
            var $toast = $('#toast');
            $toast.removeClass('toast-success toast-error toast-info').addClass('toast-' + type).text(message).fadeIn(300);
            setTimeout(function () { $toast.fadeOut(300); }, 3000);
        }

        function escapeHtml(str) {
            if (str === null || str === undefined) return '';
            return $('<span>').text(String(str)).html();
        }

        function formatDate(dateStr) {
            if (!dateStr) return '';
            var d = new Date(dateStr);
            if (isNaN(d.getTime())) return dateStr;
            return (d.getMonth() + 1).toString().padStart(2, '0') + '/' +
                   d.getDate().toString().padStart(2, '0') + '/' +
                   d.getFullYear();
        }

        function formatCurrency(val) {
            var num = parseFloat(val);
            if (isNaN(num)) return '$0.00';
            return '$' + num.toFixed(2);
        }

        function parseDateForCompare(dateStr) {
            if (!dateStr) return null;
            var d = new Date(dateStr);
            return isNaN(d.getTime()) ? null : d;
        }

        // ===== Contract Action Bar =====
        function updateContractActionBar() {
            var count = Object.keys(contractChanges).length;
            if (count > 0) {
                $('#contractChangeCount').text(count + ' unsaved change(s)');
                $('#btnSaveContracts').prop('disabled', false).text('Save All (' + count + ')');
                $('#btnCancelContracts').prop('disabled', false);
            } else {
                $('#contractChangeCount').text('No unsaved changes');
                $('#btnSaveContracts').prop('disabled', true).text('Save All');
                $('#btnCancelContracts').prop('disabled', true);
            }
        }

        function markContractChanged(id) {
            contractChanges[id] = true;
            $('#contractGrid tr#' + id).addClass('row-modified');
            updateContractActionBar();
        }

        function storeContractEdit(id, field, value) {
            if (!contractEdited[id]) contractEdited[id] = JSON.parse(JSON.stringify(contractOriginal[id] || {}));
            contractEdited[id][field] = value;
        }

        // ===== Plan Action Bar (per subgrid) =====
        function updatePlanActionBar(contractId, subgridDivId) {
            var changes = planChangesByContract[contractId] || {};
            var count = Object.keys(changes).length;
            var $bar = $('#' + subgridDivId).find('.plan-action-bar');
            if (count > 0) {
                $bar.find('.change-count').text(count + ' unsaved change(s)');
                $bar.find('.btn-save').prop('disabled', false).text('Save (' + count + ')');
                $bar.find('.btn-cancel').prop('disabled', false);
            } else {
                $bar.find('.change-count').text('No unsaved changes');
                $bar.find('.btn-save').prop('disabled', true).text('Save');
                $bar.find('.btn-cancel').prop('disabled', true);
            }
        }

        function markPlanChanged(contractId, planId, subgridDivId) {
            if (!planChangesByContract[contractId]) planChangesByContract[contractId] = {};
            planChangesByContract[contractId][planId] = true;
            var tableId = subgridDivId + '_table';
            $('#' + tableId + ' tr#' + planId).addClass('row-modified');
            updatePlanActionBar(contractId, subgridDivId);
        }

        function storePlanEdit(contractId, planId, field, value) {
            if (!planEditedByContract[contractId]) planEditedByContract[contractId] = {};
            if (!planEditedByContract[contractId][planId]) {
                var orig = (planOriginalByContract[contractId] || {})[planId] || {};
                planEditedByContract[contractId][planId] = JSON.parse(JSON.stringify(orig));
            }
            planEditedByContract[contractId][planId][field] = value;
        }

        // ===== Benefit Action Bar (per subgrid) =====
        function updateBenefitActionBar(planId, subgridDivId) {
            var changes = benefitChangesByPlan[planId] || {};
            var count = Object.keys(changes).length;
            var $bar = $('#' + subgridDivId).find('.benefit-action-bar');
            if (count > 0) {
                $bar.find('.change-count').text(count + ' unsaved change(s)');
                $bar.find('.btn-save').prop('disabled', false).text('Save (' + count + ')');
                $bar.find('.btn-cancel').prop('disabled', false);
            } else {
                $bar.find('.change-count').text('No unsaved changes');
                $bar.find('.btn-save').prop('disabled', true).text('Save');
                $bar.find('.btn-cancel').prop('disabled', true);
            }
        }

        function markBenefitChanged(planId, benefitId, subgridDivId) {
            if (!benefitChangesByPlan[planId]) benefitChangesByPlan[planId] = {};
            benefitChangesByPlan[planId][benefitId] = true;
            var tableId = subgridDivId + '_table';
            $('#' + tableId + ' tr#' + benefitId).addClass('row-modified');
            updateBenefitActionBar(planId, subgridDivId);
        }

        function storeBenefitEdit(planId, benefitId, field, value) {
            if (!benefitEditedByPlan[planId]) benefitEditedByPlan[planId] = {};
            if (!benefitEditedByPlan[planId][benefitId]) {
                var orig = (benefitOriginalByPlan[planId] || {})[benefitId] || {};
                benefitEditedByPlan[planId][benefitId] = JSON.parse(JSON.stringify(orig));
            }
            benefitEditedByPlan[planId][benefitId][field] = value;
        }

        // ===== Contract Grid Formatters =====
        function contractNameFmt(val) { return '<span class="cell-display cell-contract-name">' + escapeHtml(val) + '</span>'; }
        function contractTypeFmt(val) { return '<span class="cell-display cell-contract-type">' + escapeHtml(val || '\u2014') + '</span>'; }
        function contractStartFmt(val) { return '<span class="cell-display cell-contract-start">' + escapeHtml(formatDate(val) || '\u2014') + '</span>'; }
        function contractEndFmt(val) { return '<span class="cell-display cell-contract-end">' + escapeHtml(formatDate(val) || '\u2014') + '</span>'; }
        function contractActiveFmt(val) {
            var checked = (val === true || val === 'true') ? ' checked' : '';
            var label = (val === true || val === 'true')
                ? '<span class="status-active">Active</span>'
                : '<span class="status-inactive">Inactive</span>';
            return '<label style="cursor:pointer"><input type="checkbox" class="edit-contract-active"' + checked + ' /> ' + label + '</label>';
        }

        // ===== Plan Grid Formatters =====
        function planNameFmt(val) { return '<span class="cell-display cell-plan-name">' + escapeHtml(val) + '</span>'; }
        function planCodeFmt(val) { return '<span class="cell-display cell-plan-code">' + escapeHtml(val) + '</span>'; }
        function planEffFmt(val) { return '<span class="cell-display cell-plan-eff">' + escapeHtml(formatDate(val) || '\u2014') + '</span>'; }
        function planTermFmt(val) { return '<span class="cell-display cell-plan-term">' + escapeHtml(formatDate(val) || '\u2014') + '</span>'; }
        function planStatusFmt(val) {
            var cls = 'status-active';
            if (val === 'Pending') cls = 'status-pending';
            else if (val === 'Terminated') cls = 'status-terminated';
            return '<span class="cell-display cell-plan-status"><span class="' + cls + '">' + escapeHtml(val || '\u2014') + '</span></span>';
        }

        // ===== Benefit Grid Formatters =====
        function benefitNameFmt(val) { return '<span class="cell-display cell-benefit-name">' + escapeHtml(val) + '</span>'; }
        function benefitCoverageFmt(val) { return '<span class="cell-display cell-benefit-coverage">' + escapeHtml(val || '\u2014') + '</span>'; }
        function benefitCopayFmt(val) { return '<span class="cell-display cell-benefit-copay">' + formatCurrency(val) + '</span>'; }
        function benefitStartFmt(val) { return '<span class="cell-display cell-benefit-start">' + escapeHtml(formatDate(val) || '\u2014') + '</span>'; }
        function benefitEndFmt(val) { return '<span class="cell-display cell-benefit-end">' + escapeHtml(formatDate(val) || '\u2014') + '</span>'; }

        // ===== Initialize Contract Grid with subGrid =====
        $('#contractGrid').jqGrid({
            datatype: 'local',
            colModel: [
                { name: 'id', label: 'ID', width: 50, align: 'center', sortable: true },
                { name: 'name', label: 'Contract Name', width: 200, sortable: true, formatter: function(v) { return contractNameFmt(v); } },
                { name: 'contractType', label: 'Type', width: 120, sortable: true, formatter: function(v) { return contractTypeFmt(v); } },
                { name: 'startDate', label: 'Start Date', width: 120, sortable: true, formatter: function(v) { return contractStartFmt(v); } },
                { name: 'endDate', label: 'End Date', width: 120, sortable: true, formatter: function(v) { return contractEndFmt(v); } },
                { name: 'isActive', label: 'Active', width: 90, sortable: true, formatter: function(v) { return contractActiveFmt(v); } },
                { name: 'planCount', label: 'Plans', width: 60, align: 'center', sortable: true }
            ],
            height: 'auto',
            autowidth: true,
            rowNum: 50,
            sortname: 'id',
            sortorder: 'asc',
            subGrid: true,
            subGridRowExpanded: function (subgridDivId, contractRowId) {
                expandContractSubgrid(subgridDivId, contractRowId);
            },
            gridComplete: function () {
                var ids = Object.keys(contractChanges);
                for (var i = 0; i < ids.length; i++) {
                    $('#contractGrid tr#' + ids[i]).addClass('row-modified');
                }
            }
        });

        // ===== Expand Contract Subgrid: Load Plans =====
        function expandContractSubgrid(subgridDivId, contractRowId) {
            var contractId = parseInt(contractRowId);
            var contractData = $('#contractGrid').jqGrid('getLocalRow', contractRowId);
            var contractName = contractData ? contractData.name : '';
            var subgridTableId = subgridDivId + '_table';

            var html = '<div class="subgrid-container">' +
                '<div class="subgrid-header">' +
                    '<h3>Plans for: ' + escapeHtml(contractName) + '</h3>' +
                    '<button type="button" class="btn-add btn-add-plan" data-contract-id="' + contractId + '">+ Add Plan</button>' +
                '</div>' +
                '<table id="' + subgridTableId + '"></table>' +
                '<div class="action-bar plan-action-bar" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '">' +
                    '<span class="change-count">No unsaved changes</span>' +
                    '<button type="button" class="btn-cancel btn-cancel-plans" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '" disabled>Cancel</button>' +
                    '<button type="button" class="btn-save btn-save-plans" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '" disabled>Save</button>' +
                '</div>' +
            '</div>';

            $('#' + subgridDivId).html(html);

            // Load plans via AJAX
            $.ajax({
                url: '/WebServices/ContractService.asmx/GetPlansByContractId',
                type: 'POST',
                contentType: 'application/json; charset=utf-8',
                dataType: 'json',
                data: JSON.stringify({ contractId: contractId }),
                success: function (response) {
                    var data = response.d || response;
                    var rows = data.plans || [];

                    // Store originals
                    if (!planOriginalByContract[contractId]) planOriginalByContract[contractId] = {};
                    rows.forEach(function (r) {
                        planOriginalByContract[contractId][r.id] = JSON.parse(JSON.stringify(r));
                    });

                    $('#' + subgridTableId).jqGrid({
                        datatype: 'local',
                        data: rows,
                        colModel: [
                            { name: 'id', label: 'ID', width: 50, align: 'center', sortable: true },
                            { name: 'planName', label: 'Plan Name', width: 180, sortable: true, formatter: function(v) { return planNameFmt(v); } },
                            { name: 'planCode', label: 'Code', width: 100, sortable: true, formatter: function(v) { return planCodeFmt(v); } },
                            { name: 'effectiveDate', label: 'Effective Date', width: 120, sortable: true, formatter: function(v) { return planEffFmt(v); } },
                            { name: 'termDate', label: 'Term Date', width: 120, sortable: true, formatter: function(v) { return planTermFmt(v); } },
                            { name: 'status', label: 'Status', width: 100, sortable: true, formatter: function(v) { return planStatusFmt(v); } },
                            { name: 'benefitCount', label: 'Benefits', width: 70, align: 'center', sortable: true }
                        ],
                        height: 'auto',
                        autowidth: true,
                        rowNum: 50,
                        sortname: 'id',
                        sortorder: 'asc',
                        subGrid: true,
                        subGridRowExpanded: function (benefitSubgridDivId, planRowId) {
                            expandPlanSubgrid(benefitSubgridDivId, planRowId, contractId);
                        },
                        gridComplete: function () {
                            var changes = planChangesByContract[contractId] || {};
                            var ids = Object.keys(changes);
                            for (var i = 0; i < ids.length; i++) {
                                $('#' + subgridTableId + ' tr#' + ids[i]).addClass('row-modified');
                            }
                        }
                    });
                },
                error: function (xhr, status, error) {
                    showToast('Error loading plans: ' + error, 'error');
                }
            });
        }

        // ===== Expand Plan Subgrid: Load Benefits =====
        function expandPlanSubgrid(subgridDivId, planRowId, contractId) {
            var planId = parseInt(planRowId);
            var subgridTableId = subgridDivId + '_table';

            // Find plan name from the parent grid
            var parentTableId = $(('#' + subgridDivId)).closest('.ui-jqgrid').find('table.ui-jqgrid-btable').attr('id');
            var planData = parentTableId ? $('#' + parentTableId).jqGrid('getLocalRow', planRowId) : null;
            var planName = planData ? planData.planName : '';

            var html = '<div class="subgrid-container">' +
                '<div class="subgrid-header">' +
                    '<h3>Benefits for: ' + escapeHtml(planName) + '</h3>' +
                    '<button type="button" class="btn-add btn-add-benefit" data-plan-id="' + planId + '" data-contract-id="' + contractId + '">+ Add Benefit</button>' +
                '</div>' +
                '<table id="' + subgridTableId + '"></table>' +
                '<div class="action-bar benefit-action-bar" data-plan-id="' + planId + '" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '">' +
                    '<span class="change-count">No unsaved changes</span>' +
                    '<button type="button" class="btn-cancel btn-cancel-benefits" data-plan-id="' + planId + '" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '" disabled>Cancel</button>' +
                    '<button type="button" class="btn-save btn-save-benefits" data-plan-id="' + planId + '" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '" disabled>Save</button>' +
                '</div>' +
            '</div>';

            $('#' + subgridDivId).html(html);

            // Load benefits via AJAX
            $.ajax({
                url: '/WebServices/ContractService.asmx/GetBenefitsByPlanId',
                type: 'POST',
                contentType: 'application/json; charset=utf-8',
                dataType: 'json',
                data: JSON.stringify({ planId: planId }),
                success: function (response) {
                    var data = response.d || response;
                    var rows = data.benefits || [];

                    // Store originals
                    if (!benefitOriginalByPlan[planId]) benefitOriginalByPlan[planId] = {};
                    rows.forEach(function (r) {
                        benefitOriginalByPlan[planId][r.id] = JSON.parse(JSON.stringify(r));
                    });

                    $('#' + subgridTableId).jqGrid({
                        datatype: 'local',
                        data: rows,
                        colModel: [
                            { name: 'id', label: 'ID', width: 50, align: 'center', sortable: true },
                            { name: 'benefitName', label: 'Benefit Name', width: 180, sortable: true, formatter: function(v) { return benefitNameFmt(v); } },
                            { name: 'coverageType', label: 'Coverage Type', width: 120, sortable: true, formatter: function(v) { return benefitCoverageFmt(v); } },
                            { name: 'copayAmount', label: 'Copay ($)', width: 100, sortable: true, formatter: function(v) { return benefitCopayFmt(v); } },
                            { name: 'startDate', label: 'Start Date', width: 120, sortable: true, formatter: function(v) { return benefitStartFmt(v); } },
                            { name: 'endDate', label: 'End Date', width: 120, sortable: true, formatter: function(v) { return benefitEndFmt(v); } }
                        ],
                        height: 'auto',
                        autowidth: true,
                        rowNum: 50,
                        sortname: 'id',
                        sortorder: 'asc',
                        gridComplete: function () {
                            var changes = benefitChangesByPlan[planId] || {};
                            var ids = Object.keys(changes);
                            for (var i = 0; i < ids.length; i++) {
                                $('#' + subgridTableId + ' tr#' + ids[i]).addClass('row-modified');
                            }
                        }
                    });
                },
                error: function (xhr, status, error) {
                    showToast('Error loading benefits: ' + error, 'error');
                }
            });
        }

        // ===== Load Contracts =====
        function loadContracts() {
            $.ajax({
                url: '/WebServices/ContractService.asmx/GetContracts',
                type: 'POST',
                contentType: 'application/json; charset=utf-8',
                dataType: 'json',
                data: '{}',
                success: function (response) {
                    var data = response.d || response;
                    var rows = data.contracts || [];
                    contractOriginal = {};
                    rows.forEach(function (r) { contractOriginal[r.id] = JSON.parse(JSON.stringify(r)); });
                    $('#contractGrid').jqGrid('clearGridData').jqGrid('setGridParam', { data: rows }).trigger('reloadGrid');
                },
                error: function (xhr, status, error) {
                    showToast('Error loading contracts: ' + error, 'error');
                }
            });
        }

        // Initial load
        loadContracts();

        // ===== Contract Grid: Click-to-edit Name (delegated) =====
        $(document).on('click', '.cell-contract-name', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var currentVal = $(this).text();
            $td.html('<input type="text" class="edit-input edit-contract-name-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" />');
            $td.find('.edit-contract-name-input').focus().select();
        });

        $(document).on('blur', '.edit-contract-name-input', function () {
            var rowId = $(this).data('rowid');
            var $td = $(this).closest('td');
            var newVal = $(this).val();
            $td.html(contractNameFmt(newVal));
            storeContractEdit(rowId, 'name', newVal);
            if (contractOriginal[rowId] && newVal !== contractOriginal[rowId].name) {
                markContractChanged(rowId);
            }
        });

        $(document).on('keydown', '.edit-contract-name-input', function (e) {
            if (e.which === 13) $(this).blur();
            if (e.which === 27) {
                var rowId = $(this).data('rowid');
                var $td = $(this).closest('td');
                var origVal = contractOriginal[rowId] ? contractOriginal[rowId].name : '';
                $td.html(contractNameFmt(origVal));
            }
        });

        // ===== Contract Grid: Click-to-edit Type (dropdown) =====
        $(document).on('click', '.cell-contract-type', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var currentVal = $(this).text().trim();
            if (currentVal === '\u2014') currentVal = '';
            var html = '<select class="edit-select edit-contract-type-input" data-rowid="' + rowId + '">';
            for (var i = 0; i < contractTypes.length; i++) {
                var sel = (contractTypes[i] === currentVal) ? ' selected' : '';
                html += '<option value="' + contractTypes[i] + '"' + sel + '>' + contractTypes[i] + '</option>';
            }
            html += '</select>';
            $td.html(html);
            $td.find('.edit-contract-type-input').focus();
        });

        $(document).on('change', '.edit-contract-type-input', function () { $(this).blur(); });

        $(document).on('blur', '.edit-contract-type-input', function () {
            var rowId = $(this).data('rowid');
            var $td = $(this).closest('td');
            var newVal = $(this).val();
            $td.html(contractTypeFmt(newVal));
            storeContractEdit(rowId, 'contractType', newVal);
            if (contractOriginal[rowId] && newVal !== contractOriginal[rowId].contractType) {
                markContractChanged(rowId);
            }
        });

        // ===== Contract Grid: Click-to-edit Start Date =====
        $(document).on('click', '.cell-contract-start', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var currentVal = $(this).text().trim();
            if (currentVal === '\u2014') currentVal = '';
            $td.html('<input type="text" class="edit-date edit-contract-start-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" placeholder="MM/DD/YYYY" />');
            var $input = $td.find('.edit-contract-start-input');
            flatpickr($input.get(0), {
                dateFormat: 'm/d/Y',
                allowInput: true,
                defaultDate: currentVal || null,
                onClose: function (selectedDates, dateStr) {
                    var rid = $input.data('rowid');
                    var $c = $input.closest('td');
                    $c.html(contractStartFmt(dateStr));
                    storeContractEdit(rid, 'startDate', dateStr || null);
                    if (contractOriginal[rid] && dateStr !== formatDate(contractOriginal[rid].startDate)) {
                        markContractChanged(rid);
                    }
                }
            });
            $input.get(0)._flatpickr.open();
        });

        // ===== Contract Grid: Click-to-edit End Date =====
        $(document).on('click', '.cell-contract-end', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var currentVal = $(this).text().trim();
            if (currentVal === '\u2014') currentVal = '';
            $td.html('<input type="text" class="edit-date edit-contract-end-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" placeholder="MM/DD/YYYY" />');
            var $input = $td.find('.edit-contract-end-input');
            flatpickr($input.get(0), {
                dateFormat: 'm/d/Y',
                allowInput: true,
                defaultDate: currentVal || null,
                onClose: function (selectedDates, dateStr) {
                    var rid = $input.data('rowid');
                    var $c = $input.closest('td');
                    $c.html(contractEndFmt(dateStr));
                    storeContractEdit(rid, 'endDate', dateStr || null);
                    if (contractOriginal[rid] && dateStr !== formatDate(contractOriginal[rid].endDate)) {
                        markContractChanged(rid);
                    }
                }
            });
            $input.get(0)._flatpickr.open();
        });

        // ===== Contract Grid: Active checkbox =====
        $(document).on('change', '.edit-contract-active', function () {
            var rowId = $(this).closest('tr').attr('id');
            var isActive = $(this).is(':checked');
            $(this).siblings('span').replaceWith(
                isActive ? '<span class="status-active">Active</span>' : '<span class="status-inactive">Inactive</span>'
            );
            storeContractEdit(rowId, 'isActive', isActive);
            markContractChanged(rowId);
        });

        // ===== Plan Grid: Click-to-edit Plan Name (delegated for dynamic subgrids) =====
        $(document).on('click', '.cell-plan-name', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var contractId = $(this).closest('.subgrid-container').find('.plan-action-bar').data('contract-id');
            var subgridDivId = $(this).closest('.subgrid-container').find('.plan-action-bar').data('subgrid-div');
            var currentVal = $(this).text();
            $td.html('<input type="text" class="edit-input edit-plan-name-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '" />');
            $td.find('.edit-plan-name-input').focus().select();
        });

        $(document).on('blur', '.edit-plan-name-input', function () {
            var rowId = $(this).data('rowid');
            var contractId = $(this).data('contract-id');
            var subgridDivId = $(this).data('subgrid-div');
            var $td = $(this).closest('td');
            var newVal = $(this).val();
            $td.html(planNameFmt(newVal));
            storePlanEdit(contractId, rowId, 'planName', newVal);
            var orig = (planOriginalByContract[contractId] || {})[rowId];
            if (orig && newVal !== orig.planName) {
                markPlanChanged(contractId, rowId, subgridDivId);
            }
        });

        $(document).on('keydown', '.edit-plan-name-input', function (e) {
            if (e.which === 13) $(this).blur();
            if (e.which === 27) {
                var rowId = $(this).data('rowid');
                var contractId = $(this).data('contract-id');
                var $td = $(this).closest('td');
                var orig = (planOriginalByContract[contractId] || {})[rowId];
                $td.html(planNameFmt(orig ? orig.planName : ''));
            }
        });

        // ===== Plan Grid: Click-to-edit Plan Code =====
        $(document).on('click', '.cell-plan-code', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var contractId = $(this).closest('.subgrid-container').find('.plan-action-bar').data('contract-id');
            var subgridDivId = $(this).closest('.subgrid-container').find('.plan-action-bar').data('subgrid-div');
            var currentVal = $(this).text();
            $td.html('<input type="text" class="edit-input edit-plan-code-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '" />');
            $td.find('.edit-plan-code-input').focus().select();
        });

        $(document).on('blur', '.edit-plan-code-input', function () {
            var rowId = $(this).data('rowid');
            var contractId = $(this).data('contract-id');
            var subgridDivId = $(this).data('subgrid-div');
            var $td = $(this).closest('td');
            var newVal = $(this).val();
            $td.html(planCodeFmt(newVal));
            storePlanEdit(contractId, rowId, 'planCode', newVal);
            var orig = (planOriginalByContract[contractId] || {})[rowId];
            if (orig && newVal !== orig.planCode) {
                markPlanChanged(contractId, rowId, subgridDivId);
            }
        });

        $(document).on('keydown', '.edit-plan-code-input', function (e) {
            if (e.which === 13) $(this).blur();
            if (e.which === 27) {
                var rowId = $(this).data('rowid');
                var contractId = $(this).data('contract-id');
                var $td = $(this).closest('td');
                var orig = (planOriginalByContract[contractId] || {})[rowId];
                $td.html(planCodeFmt(orig ? orig.planCode : ''));
            }
        });

        // ===== Plan Grid: Click-to-edit Effective Date =====
        $(document).on('click', '.cell-plan-eff', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var contractId = $(this).closest('.subgrid-container').find('.plan-action-bar').data('contract-id');
            var subgridDivId = $(this).closest('.subgrid-container').find('.plan-action-bar').data('subgrid-div');
            var currentVal = $(this).text().trim();
            if (currentVal === '\u2014') currentVal = '';
            $td.html('<input type="text" class="edit-date edit-plan-eff-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '" placeholder="MM/DD/YYYY" />');
            var $input = $td.find('.edit-plan-eff-input');
            flatpickr($input.get(0), {
                dateFormat: 'm/d/Y',
                allowInput: true,
                defaultDate: currentVal || null,
                onClose: function (selectedDates, dateStr) {
                    var rid = $input.data('rowid');
                    var cid = $input.data('contract-id');
                    var sgid = $input.data('subgrid-div');
                    var $c = $input.closest('td');
                    $c.html(planEffFmt(dateStr));
                    storePlanEdit(cid, rid, 'effectiveDate', dateStr || null);
                    var orig = (planOriginalByContract[cid] || {})[rid];
                    if (orig && dateStr !== formatDate(orig.effectiveDate)) {
                        markPlanChanged(cid, rid, sgid);
                    }
                }
            });
            $input.get(0)._flatpickr.open();
        });

        // ===== Plan Grid: Click-to-edit Term Date =====
        $(document).on('click', '.cell-plan-term', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var contractId = $(this).closest('.subgrid-container').find('.plan-action-bar').data('contract-id');
            var subgridDivId = $(this).closest('.subgrid-container').find('.plan-action-bar').data('subgrid-div');
            var currentVal = $(this).text().trim();
            if (currentVal === '\u2014') currentVal = '';
            $td.html('<input type="text" class="edit-date edit-plan-term-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '" placeholder="MM/DD/YYYY" />');
            var $input = $td.find('.edit-plan-term-input');
            flatpickr($input.get(0), {
                dateFormat: 'm/d/Y',
                allowInput: true,
                defaultDate: currentVal || null,
                onClose: function (selectedDates, dateStr) {
                    var rid = $input.data('rowid');
                    var cid = $input.data('contract-id');
                    var sgid = $input.data('subgrid-div');
                    var $c = $input.closest('td');
                    $c.html(planTermFmt(dateStr));
                    storePlanEdit(cid, rid, 'termDate', dateStr || null);
                    var orig = (planOriginalByContract[cid] || {})[rid];
                    if (orig && dateStr !== formatDate(orig.termDate)) {
                        markPlanChanged(cid, rid, sgid);
                    }
                }
            });
            $input.get(0)._flatpickr.open();
        });

        // ===== Plan Grid: Click-to-edit Status (dropdown) =====
        $(document).on('click', '.cell-plan-status', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var contractId = $(this).closest('.subgrid-container').find('.plan-action-bar').data('contract-id');
            var subgridDivId = $(this).closest('.subgrid-container').find('.plan-action-bar').data('subgrid-div');
            var currentVal = $(this).find('span').text().trim();
            if (currentVal === '\u2014') currentVal = '';
            var html = '<select class="edit-select edit-plan-status-input" data-rowid="' + rowId + '" data-contract-id="' + contractId + '" data-subgrid-div="' + subgridDivId + '">';
            for (var i = 0; i < planStatuses.length; i++) {
                var sel = (planStatuses[i] === currentVal) ? ' selected' : '';
                html += '<option value="' + planStatuses[i] + '"' + sel + '>' + planStatuses[i] + '</option>';
            }
            html += '</select>';
            $td.html(html);
            $td.find('.edit-plan-status-input').focus();
        });

        $(document).on('change', '.edit-plan-status-input', function () { $(this).blur(); });

        $(document).on('blur', '.edit-plan-status-input', function () {
            var rowId = $(this).data('rowid');
            var contractId = $(this).data('contract-id');
            var subgridDivId = $(this).data('subgrid-div');
            var $td = $(this).closest('td');
            var newVal = $(this).val();
            $td.html(planStatusFmt(newVal));
            storePlanEdit(contractId, rowId, 'status', newVal);
            var orig = (planOriginalByContract[contractId] || {})[rowId];
            if (orig && newVal !== orig.status) {
                markPlanChanged(contractId, rowId, subgridDivId);
            }
        });

        // ===== Benefit Grid: Click-to-edit Benefit Name =====
        $(document).on('click', '.cell-benefit-name', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var planId = $(this).closest('.subgrid-container').find('.benefit-action-bar').data('plan-id');
            var subgridDivId = $(this).closest('.subgrid-container').find('.benefit-action-bar').data('subgrid-div');
            var currentVal = $(this).text();
            $td.html('<input type="text" class="edit-input edit-benefit-name-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" data-plan-id="' + planId + '" data-subgrid-div="' + subgridDivId + '" />');
            $td.find('.edit-benefit-name-input').focus().select();
        });

        $(document).on('blur', '.edit-benefit-name-input', function () {
            var rowId = $(this).data('rowid');
            var planId = $(this).data('plan-id');
            var subgridDivId = $(this).data('subgrid-div');
            var $td = $(this).closest('td');
            var newVal = $(this).val();
            $td.html(benefitNameFmt(newVal));
            storeBenefitEdit(planId, rowId, 'benefitName', newVal);
            var orig = (benefitOriginalByPlan[planId] || {})[rowId];
            if (orig && newVal !== orig.benefitName) {
                markBenefitChanged(planId, rowId, subgridDivId);
            }
        });

        $(document).on('keydown', '.edit-benefit-name-input', function (e) {
            if (e.which === 13) $(this).blur();
            if (e.which === 27) {
                var rowId = $(this).data('rowid');
                var planId = $(this).data('plan-id');
                var $td = $(this).closest('td');
                var orig = (benefitOriginalByPlan[planId] || {})[rowId];
                $td.html(benefitNameFmt(orig ? orig.benefitName : ''));
            }
        });

        // ===== Benefit Grid: Click-to-edit Coverage Type (dropdown) =====
        $(document).on('click', '.cell-benefit-coverage', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var planId = $(this).closest('.subgrid-container').find('.benefit-action-bar').data('plan-id');
            var subgridDivId = $(this).closest('.subgrid-container').find('.benefit-action-bar').data('subgrid-div');
            var currentVal = $(this).text().trim();
            if (currentVal === '\u2014') currentVal = '';
            var html = '<select class="edit-select edit-benefit-coverage-input" data-rowid="' + rowId + '" data-plan-id="' + planId + '" data-subgrid-div="' + subgridDivId + '">';
            for (var i = 0; i < coverageTypes.length; i++) {
                var sel = (coverageTypes[i] === currentVal) ? ' selected' : '';
                html += '<option value="' + coverageTypes[i] + '"' + sel + '>' + coverageTypes[i] + '</option>';
            }
            html += '</select>';
            $td.html(html);
            $td.find('.edit-benefit-coverage-input').focus();
        });

        $(document).on('change', '.edit-benefit-coverage-input', function () { $(this).blur(); });

        $(document).on('blur', '.edit-benefit-coverage-input', function () {
            var rowId = $(this).data('rowid');
            var planId = $(this).data('plan-id');
            var subgridDivId = $(this).data('subgrid-div');
            var $td = $(this).closest('td');
            var newVal = $(this).val();
            $td.html(benefitCoverageFmt(newVal));
            storeBenefitEdit(planId, rowId, 'coverageType', newVal);
            var orig = (benefitOriginalByPlan[planId] || {})[rowId];
            if (orig && newVal !== orig.coverageType) {
                markBenefitChanged(planId, rowId, subgridDivId);
            }
        });

        // ===== Benefit Grid: Click-to-edit Copay Amount =====
        $(document).on('click', '.cell-benefit-copay', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var planId = $(this).closest('.subgrid-container').find('.benefit-action-bar').data('plan-id');
            var subgridDivId = $(this).closest('.subgrid-container').find('.benefit-action-bar').data('subgrid-div');
            var currentVal = $(this).text().replace('$', '').trim();
            $td.html('<input type="number" class="edit-input edit-benefit-copay-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" data-plan-id="' + planId + '" data-subgrid-div="' + subgridDivId + '" min="0" step="0.01" />');
            $td.find('.edit-benefit-copay-input').focus().select();
        });

        $(document).on('blur', '.edit-benefit-copay-input', function () {
            var rowId = $(this).data('rowid');
            var planId = $(this).data('plan-id');
            var subgridDivId = $(this).data('subgrid-div');
            var $td = $(this).closest('td');
            var newVal = parseFloat($(this).val()) || 0;
            if (newVal < 0) newVal = 0;
            $td.html(benefitCopayFmt(newVal));
            storeBenefitEdit(planId, rowId, 'copayAmount', newVal);
            var orig = (benefitOriginalByPlan[planId] || {})[rowId];
            if (orig && newVal !== orig.copayAmount) {
                markBenefitChanged(planId, rowId, subgridDivId);
            }
        });

        $(document).on('keydown', '.edit-benefit-copay-input', function (e) {
            if (e.which === 13) $(this).blur();
            if (e.which === 27) {
                var rowId = $(this).data('rowid');
                var planId = $(this).data('plan-id');
                var $td = $(this).closest('td');
                var orig = (benefitOriginalByPlan[planId] || {})[rowId];
                $td.html(benefitCopayFmt(orig ? orig.copayAmount : 0));
            }
        });

        // ===== Benefit Grid: Click-to-edit Start Date =====
        $(document).on('click', '.cell-benefit-start', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var planId = $(this).closest('.subgrid-container').find('.benefit-action-bar').data('plan-id');
            var subgridDivId = $(this).closest('.subgrid-container').find('.benefit-action-bar').data('subgrid-div');
            var currentVal = $(this).text().trim();
            if (currentVal === '\u2014') currentVal = '';
            $td.html('<input type="text" class="edit-date edit-benefit-start-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" data-plan-id="' + planId + '" data-subgrid-div="' + subgridDivId + '" placeholder="MM/DD/YYYY" />');
            var $input = $td.find('.edit-benefit-start-input');
            flatpickr($input.get(0), {
                dateFormat: 'm/d/Y',
                allowInput: true,
                defaultDate: currentVal || null,
                onClose: function (selectedDates, dateStr) {
                    var rid = $input.data('rowid');
                    var pid = $input.data('plan-id');
                    var sgid = $input.data('subgrid-div');
                    var $c = $input.closest('td');
                    $c.html(benefitStartFmt(dateStr));
                    storeBenefitEdit(pid, rid, 'startDate', dateStr || null);
                    var orig = (benefitOriginalByPlan[pid] || {})[rid];
                    if (orig && dateStr !== formatDate(orig.startDate)) {
                        markBenefitChanged(pid, rid, sgid);
                    }
                }
            });
            $input.get(0)._flatpickr.open();
        });

        // ===== Benefit Grid: Click-to-edit End Date =====
        $(document).on('click', '.cell-benefit-end', function (e) {
            e.stopPropagation();
            var $td = $(this).closest('td');
            var rowId = $(this).closest('tr').attr('id');
            var planId = $(this).closest('.subgrid-container').find('.benefit-action-bar').data('plan-id');
            var subgridDivId = $(this).closest('.subgrid-container').find('.benefit-action-bar').data('subgrid-div');
            var currentVal = $(this).text().trim();
            if (currentVal === '\u2014') currentVal = '';
            $td.html('<input type="text" class="edit-date edit-benefit-end-input" value="' + escapeHtml(currentVal) + '" data-rowid="' + rowId + '" data-plan-id="' + planId + '" data-subgrid-div="' + subgridDivId + '" placeholder="MM/DD/YYYY" />');
            var $input = $td.find('.edit-benefit-end-input');
            flatpickr($input.get(0), {
                dateFormat: 'm/d/Y',
                allowInput: true,
                defaultDate: currentVal || null,
                onClose: function (selectedDates, dateStr) {
                    var rid = $input.data('rowid');
                    var pid = $input.data('plan-id');
                    var sgid = $input.data('subgrid-div');
                    var $c = $input.closest('td');
                    $c.html(benefitEndFmt(dateStr));
                    storeBenefitEdit(pid, rid, 'endDate', dateStr || null);
                    var orig = (benefitOriginalByPlan[pid] || {})[rid];
                    if (orig && dateStr !== formatDate(orig.endDate)) {
                        markBenefitChanged(pid, rid, sgid);
                    }
                }
            });
            $input.get(0)._flatpickr.open();
        });

        // ===== Batch Save Contracts =====
        $('#btnSaveContracts').on('click', function () {
            var contracts = [];
            var ids = Object.keys(contractChanges);
            for (var i = 0; i < ids.length; i++) {
                var id = ids[i];
                var data = contractEdited[id] || contractOriginal[id];
                if (data) {
                    if (!data.name || data.name.length < 3) {
                        showToast('Contract name must be at least 3 characters (ID: ' + id + ').', 'error');
                        return;
                    }
                    var sd = parseDateForCompare(data.startDate);
                    var ed = parseDateForCompare(data.endDate);
                    if (sd && ed && sd >= ed) {
                        showToast('Contract "' + data.name + '": Start Date must be before End Date.', 'error');
                        return;
                    }
                    contracts.push({
                        id: parseInt(id),
                        name: data.name,
                        contractType: data.contractType,
                        startDate: data.startDate || null,
                        endDate: data.endDate || null,
                        isActive: data.isActive,
                        planCount: data.planCount || 0
                    });
                }
            }
            if (contracts.length === 0) { showToast('No changes to save.', 'info'); return; }

            $('#btnSaveContracts').prop('disabled', true).text('Saving...');
            $.ajax({
                url: '/WebServices/ContractService.asmx/BatchSaveContracts',
                type: 'POST',
                contentType: 'application/json; charset=utf-8',
                dataType: 'json',
                data: JSON.stringify({ contracts: contracts }),
                success: function (response) {
                    var result = response.d || response;
                    if (result.success) {
                        showToast('Successfully saved ' + (result.savedCount || contracts.length) + ' contract(s).', 'success');
                        contractChanges = {};
                        contractEdited = {};
                        updateContractActionBar();
                        loadContracts();
                    } else {
                        showToast(result.message || 'Error saving contracts.', 'error');
                    }
                    $('#btnSaveContracts').prop('disabled', false);
                    updateContractActionBar();
                },
                error: function (xhr, status, error) {
                    showToast('Error saving contracts: ' + error, 'error');
                    $('#btnSaveContracts').prop('disabled', false);
                    updateContractActionBar();
                }
            });
        });

        // ===== Cancel Contracts =====
        $('#btnCancelContracts').on('click', function () {
            contractChanges = {};
            contractEdited = {};
            updateContractActionBar();
            loadContracts();
            showToast('Contract changes discarded.', 'info');
        });

        // ===== Batch Save Plans (delegated) =====
        $(document).on('click', '.btn-save-plans', function () {
            var contractId = $(this).data('contract-id');
            var subgridDivId = $(this).data('subgrid-div');
            var changes = planChangesByContract[contractId] || {};
            var plans = [];
            var ids = Object.keys(changes);
            for (var i = 0; i < ids.length; i++) {
                var id = ids[i];
                var edited = (planEditedByContract[contractId] || {})[id];
                var orig = (planOriginalByContract[contractId] || {})[id];
                var data = edited || orig;
                if (data) {
                    if (!data.planName || data.planName.length < 3) {
                        showToast('Plan name must be at least 3 characters (ID: ' + id + ').', 'error');
                        return;
                    }
                    var sd = parseDateForCompare(data.effectiveDate);
                    var ed = parseDateForCompare(data.termDate);
                    if (sd && ed && sd >= ed) {
                        showToast('Plan "' + data.planName + '": Effective Date must be before Term Date.', 'error');
                        return;
                    }
                    plans.push({
                        id: parseInt(id),
                        contractId: contractId,
                        planName: data.planName,
                        planCode: data.planCode,
                        effectiveDate: data.effectiveDate || null,
                        termDate: data.termDate || null,
                        status: data.status,
                        benefitCount: data.benefitCount || 0
                    });
                }
            }
            if (plans.length === 0) { showToast('No changes to save.', 'info'); return; }

            var $btn = $(this);
            $btn.prop('disabled', true).text('Saving...');
            $.ajax({
                url: '/WebServices/ContractService.asmx/BatchSavePlans',
                type: 'POST',
                contentType: 'application/json; charset=utf-8',
                dataType: 'json',
                data: JSON.stringify({ plans: plans }),
                success: function (response) {
                    var result = response.d || response;
                    if (result.success) {
                        showToast('Successfully saved ' + (result.savedCount || plans.length) + ' plan(s).', 'success');
                        planChangesByContract[contractId] = {};
                        planEditedByContract[contractId] = {};
                        updatePlanActionBar(contractId, subgridDivId);
                        // Collapse and re-expand to reload
                        $('#contractGrid').jqGrid('collapseSubGridRow', contractId);
                        $('#contractGrid').jqGrid('expandSubGridRow', contractId);
                        loadContracts(); // refresh plan counts
                    } else {
                        showToast(result.message || 'Error saving plans.', 'error');
                    }
                    $btn.prop('disabled', false);
                    updatePlanActionBar(contractId, subgridDivId);
                },
                error: function (xhr, status, error) {
                    showToast('Error saving plans: ' + error, 'error');
                    $btn.prop('disabled', false);
                    updatePlanActionBar(contractId, subgridDivId);
                }
            });
        });

        // ===== Cancel Plans (delegated) =====
        $(document).on('click', '.btn-cancel-plans', function () {
            var contractId = $(this).data('contract-id');
            var subgridDivId = $(this).data('subgrid-div');
            planChangesByContract[contractId] = {};
            planEditedByContract[contractId] = {};
            updatePlanActionBar(contractId, subgridDivId);
            // Collapse and re-expand to reload
            $('#contractGrid').jqGrid('collapseSubGridRow', contractId);
            $('#contractGrid').jqGrid('expandSubGridRow', contractId);
            showToast('Plan changes discarded.', 'info');
        });

        // ===== Batch Save Benefits (delegated) =====
        $(document).on('click', '.btn-save-benefits', function () {
            var planId = $(this).data('plan-id');
            var contractId = $(this).data('contract-id');
            var subgridDivId = $(this).data('subgrid-div');
            var changes = benefitChangesByPlan[planId] || {};
            var benefits = [];
            var ids = Object.keys(changes);
            for (var i = 0; i < ids.length; i++) {
                var id = ids[i];
                var edited = (benefitEditedByPlan[planId] || {})[id];
                var orig = (benefitOriginalByPlan[planId] || {})[id];
                var data = edited || orig;
                if (data) {
                    if (!data.benefitName || data.benefitName.length < 3) {
                        showToast('Benefit name must be at least 3 characters (ID: ' + id + ').', 'error');
                        return;
                    }
                    if (data.copayAmount < 0) {
                        showToast('Benefit "' + data.benefitName + '": Copay must be >= 0.', 'error');
                        return;
                    }
                    var sd = parseDateForCompare(data.startDate);
                    var ed = parseDateForCompare(data.endDate);
                    if (sd && ed && sd >= ed) {
                        showToast('Benefit "' + data.benefitName + '": Start Date must be before End Date.', 'error');
                        return;
                    }
                    benefits.push({
                        id: parseInt(id),
                        planId: planId,
                        benefitName: data.benefitName,
                        coverageType: data.coverageType,
                        copayAmount: parseFloat(data.copayAmount) || 0,
                        startDate: data.startDate || null,
                        endDate: data.endDate || null
                    });
                }
            }
            if (benefits.length === 0) { showToast('No changes to save.', 'info'); return; }

            var $btn = $(this);
            $btn.prop('disabled', true).text('Saving...');
            $.ajax({
                url: '/WebServices/ContractService.asmx/BatchSaveBenefits',
                type: 'POST',
                contentType: 'application/json; charset=utf-8',
                dataType: 'json',
                data: JSON.stringify({ benefits: benefits }),
                success: function (response) {
                    var result = response.d || response;
                    if (result.success) {
                        showToast('Successfully saved ' + (result.savedCount || benefits.length) + ' benefit(s).', 'success');
                        benefitChangesByPlan[planId] = {};
                        benefitEditedByPlan[planId] = {};
                        updateBenefitActionBar(planId, subgridDivId);
                        // Reload the benefit subgrid
                        var parentSubgridDivId = $('#' + subgridDivId).closest('[id$="_t"]').attr('id') ||
                                                 $('#' + subgridDivId).closest('td.subgrid-cell').prev().find('[id]').attr('id');
                        // Simplest: collapse and re-expand the plan's parent contract subgrid
                        $('#contractGrid').jqGrid('collapseSubGridRow', contractId);
                        $('#contractGrid').jqGrid('expandSubGridRow', contractId);
                    } else {
                        showToast(result.message || 'Error saving benefits.', 'error');
                    }
                    $btn.prop('disabled', false);
                    updateBenefitActionBar(planId, subgridDivId);
                },
                error: function (xhr, status, error) {
                    showToast('Error saving benefits: ' + error, 'error');
                    $btn.prop('disabled', false);
                    updateBenefitActionBar(planId, subgridDivId);
                }
            });
        });

        // ===== Cancel Benefits (delegated) =====
        $(document).on('click', '.btn-cancel-benefits', function () {
            var planId = $(this).data('plan-id');
            var contractId = $(this).data('contract-id');
            var subgridDivId = $(this).data('subgrid-div');
            benefitChangesByPlan[planId] = {};
            benefitEditedByPlan[planId] = {};
            updateBenefitActionBar(planId, subgridDivId);
            // Collapse and re-expand the contract subgrid to reload
            $('#contractGrid').jqGrid('collapseSubGridRow', contractId);
            $('#contractGrid').jqGrid('expandSubGridRow', contractId);
            showToast('Benefit changes discarded.', 'info');
        });

        // ===== Add Contract Dialog =====
        var dlgContractStartPicker = null;
        var dlgContractEndPicker = null;

        $('#btnAddContract').on('click', function () {
            $('#dlgContractName').val('');
            $('#dlgContractType').val('Commercial');
            $('#dlgContractStart').val('');
            $('#dlgContractEnd').val('');
            $('#dlgContractActive').prop('checked', true);
            $('.validation-msg').hide();

            if (dlgContractStartPicker) dlgContractStartPicker.destroy();
            if (dlgContractEndPicker) dlgContractEndPicker.destroy();
            dlgContractStartPicker = flatpickr('#dlgContractStart', { dateFormat: 'm/d/Y', allowInput: true });
            dlgContractEndPicker = flatpickr('#dlgContractEnd', { dateFormat: 'm/d/Y', allowInput: true });

            $('#dlgAddContract').dialog({
                modal: true,
                width: 420,
                buttons: {
                    'Save': function () {
                        var name = $('#dlgContractName').val().trim();
                        var type = $('#dlgContractType').val();
                        var start = $('#dlgContractStart').val();
                        var end = $('#dlgContractEnd').val();
                        var active = $('#dlgContractActive').is(':checked');

                        var valid = true;
                        if (name.length < 3) { $('#valContractName').show(); valid = false; } else { $('#valContractName').hide(); }
                        var sd = parseDateForCompare(start);
                        var ed = parseDateForCompare(end);
                        if (sd && ed && sd >= ed) { $('#valContractDates').show(); valid = false; } else { $('#valContractDates').hide(); }
                        if (!valid) return;

                        var contract = { id: 0, name: name, contractType: type, startDate: start || null, endDate: end || null, isActive: active, planCount: 0 };
                        $.ajax({
                            url: '/WebServices/ContractService.asmx/SaveContract',
                            type: 'POST',
                            contentType: 'application/json; charset=utf-8',
                            dataType: 'json',
                            data: JSON.stringify(contract),
                            success: function (response) {
                                var result = response.d || response;
                                if (result.success) {
                                    showToast('Contract added successfully.', 'success');
                                    $('#dlgAddContract').dialog('close');
                                    loadContracts();
                                } else {
                                    showToast(result.message || 'Error adding contract.', 'error');
                                }
                            },
                            error: function (xhr, status, error) {
                                showToast('Error adding contract: ' + error, 'error');
                            }
                        });
                    },
                    'Cancel': function () { $(this).dialog('close'); }
                }
            });
        });

        // ===== Add Plan Dialog (delegated for subgrid buttons) =====
        var dlgPlanEffPicker = null;
        var dlgPlanTermPicker = null;

        $(document).on('click', '.btn-add-plan', function () {
            currentAddPlanContractId = $(this).data('contract-id');

            $('#dlgPlanName').val('');
            $('#dlgPlanCode').val('');
            $('#dlgPlanEffective').val('');
            $('#dlgPlanTerm').val('');
            $('#dlgPlanStatus').val('Active');
            $('.validation-msg').hide();

            if (dlgPlanEffPicker) dlgPlanEffPicker.destroy();
            if (dlgPlanTermPicker) dlgPlanTermPicker.destroy();
            dlgPlanEffPicker = flatpickr('#dlgPlanEffective', { dateFormat: 'm/d/Y', allowInput: true });
            dlgPlanTermPicker = flatpickr('#dlgPlanTerm', { dateFormat: 'm/d/Y', allowInput: true });

            $('#dlgAddPlan').dialog({
                modal: true,
                width: 420,
                buttons: {
                    'Save': function () {
                        var name = $('#dlgPlanName').val().trim();
                        var code = $('#dlgPlanCode').val().trim();
                        var eff = $('#dlgPlanEffective').val();
                        var term = $('#dlgPlanTerm').val();
                        var status = $('#dlgPlanStatus').val();

                        var valid = true;
                        if (name.length < 3) { $('#valPlanName').show(); valid = false; } else { $('#valPlanName').hide(); }
                        if (!code) { $('#valPlanCode').show(); valid = false; } else { $('#valPlanCode').hide(); }
                        var sd = parseDateForCompare(eff);
                        var ed = parseDateForCompare(term);
                        if (sd && ed && sd >= ed) { $('#valPlanDates').show(); valid = false; } else { $('#valPlanDates').hide(); }
                        if (!valid) return;

                        var plan = { id: 0, contractId: currentAddPlanContractId, planName: name, planCode: code, effectiveDate: eff || null, termDate: term || null, status: status, benefitCount: 0 };
                        $.ajax({
                            url: '/WebServices/ContractService.asmx/SavePlan',
                            type: 'POST',
                            contentType: 'application/json; charset=utf-8',
                            dataType: 'json',
                            data: JSON.stringify(plan),
                            success: function (response) {
                                var result = response.d || response;
                                if (result.success) {
                                    showToast('Plan added successfully.', 'success');
                                    $('#dlgAddPlan').dialog('close');
                                    // Collapse and re-expand to reload plans
                                    $('#contractGrid').jqGrid('collapseSubGridRow', currentAddPlanContractId);
                                    $('#contractGrid').jqGrid('expandSubGridRow', currentAddPlanContractId);
                                    loadContracts(); // refresh plan count
                                } else {
                                    showToast(result.message || 'Error adding plan.', 'error');
                                }
                            },
                            error: function (xhr, status, error) {
                                showToast('Error adding plan: ' + error, 'error');
                            }
                        });
                    },
                    'Cancel': function () { $(this).dialog('close'); }
                }
            });
        });

        // ===== Add Benefit Dialog (delegated for subgrid buttons) =====
        var dlgBenefitStartPicker = null;
        var dlgBenefitEndPicker = null;

        $(document).on('click', '.btn-add-benefit', function () {
            currentAddBenefitPlanId = $(this).data('plan-id');
            currentAddBenefitContractId = $(this).data('contract-id');

            $('#dlgBenefitName').val('');
            $('#dlgBenefitCoverage').val('Medical');
            $('#dlgBenefitCopay').val('0');
            $('#dlgBenefitStart').val('');
            $('#dlgBenefitEnd').val('');
            $('.validation-msg').hide();

            if (dlgBenefitStartPicker) dlgBenefitStartPicker.destroy();
            if (dlgBenefitEndPicker) dlgBenefitEndPicker.destroy();
            dlgBenefitStartPicker = flatpickr('#dlgBenefitStart', { dateFormat: 'm/d/Y', allowInput: true });
            dlgBenefitEndPicker = flatpickr('#dlgBenefitEnd', { dateFormat: 'm/d/Y', allowInput: true });

            $('#dlgAddBenefit').dialog({
                modal: true,
                width: 420,
                buttons: {
                    'Save': function () {
                        var name = $('#dlgBenefitName').val().trim();
                        var coverage = $('#dlgBenefitCoverage').val();
                        var copay = parseFloat($('#dlgBenefitCopay').val()) || 0;
                        var start = $('#dlgBenefitStart').val();
                        var end = $('#dlgBenefitEnd').val();

                        var valid = true;
                        if (name.length < 3) { $('#valBenefitName').show(); valid = false; } else { $('#valBenefitName').hide(); }
                        if (copay < 0) { $('#valBenefitCopay').show(); valid = false; } else { $('#valBenefitCopay').hide(); }
                        var sd = parseDateForCompare(start);
                        var ed = parseDateForCompare(end);
                        if (sd && ed && sd >= ed) { $('#valBenefitDates').show(); valid = false; } else { $('#valBenefitDates').hide(); }
                        if (!valid) return;

                        var benefit = { id: 0, planId: currentAddBenefitPlanId, benefitName: name, coverageType: coverage, copayAmount: copay, startDate: start || null, endDate: end || null };
                        $.ajax({
                            url: '/WebServices/ContractService.asmx/SaveBenefit',
                            type: 'POST',
                            contentType: 'application/json; charset=utf-8',
                            dataType: 'json',
                            data: JSON.stringify(benefit),
                            success: function (response) {
                                var result = response.d || response;
                                if (result.success) {
                                    showToast('Benefit added successfully.', 'success');
                                    $('#dlgAddBenefit').dialog('close');
                                    // Collapse and re-expand the contract subgrid to reload
                                    $('#contractGrid').jqGrid('collapseSubGridRow', currentAddBenefitContractId);
                                    $('#contractGrid').jqGrid('expandSubGridRow', currentAddBenefitContractId);
                                } else {
                                    showToast(result.message || 'Error adding benefit.', 'error');
                                }
                            },
                            error: function (xhr, status, error) {
                                showToast('Error adding benefit: ' + error, 'error');
                            }
                        });
                    },
                    'Cancel': function () { $(this).dialog('close'); }
                }
            });
        });

    }); // end document.ready
    </script>
</body>
</html>
