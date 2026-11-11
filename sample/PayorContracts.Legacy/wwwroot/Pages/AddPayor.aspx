<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head>
    <title>Add Payor</title>
    <script src="https://code.jquery.com/jquery-3.7.1.min.js" integrity="sha384-1H217gwSVyLSIfaLxHbE7dRb3v4mYCKbpQvzx0cegeju1MVsGrX5xXxAvs/HgeFs" crossorigin="anonymous"></script>
    <script src="https://cdn.jsdelivr.net/npm/jquery-validation@1.19.5/dist/jquery.validate.min.js" integrity="sha384-aEDtD4n2FLrMdE9psop0SHdNyy/W9cBjH22rSRp+3wPHd62Y32uijc0H2eLmgaSn" crossorigin="anonymous"></script>
    <script src="https://cdn.jsdelivr.net/npm/jquery-validation@1.19.5/dist/additional-methods.min.js" integrity="sha384-kxI94MBt6egf2HqINJ9x8sPSfqxudviPxgttYjlTFaPREJb/gmAOgTr/GYie5ung" crossorigin="anonymous"></script>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background: #f5f5f5; }
        .page-header { background: #2c3e50; color: white; padding: 15px 20px; margin: -20px -20px 20px -20px; }
        .page-header h1 { margin: 0; font-size: 1.5em; }
        .form-container { background: white; padding: 20px; border: 1px solid #ddd; border-radius: 4px; max-width: 600px; margin-top: 20px; }
        .form-group { margin-bottom: 15px; }
        .form-group label { display: block; font-weight: 600; margin-bottom: 5px; font-size: 0.9em; }
        .form-group input, .form-group select { width: 100%; padding: 8px 10px; border: 1px solid #ccc; border-radius: 3px; box-sizing: border-box; }
        .form-group input.error, .form-group select.error { border-color: #e74c3c; }
        label.error { color: #e74c3c; font-size: 0.85em; margin-top: 4px; display: block; }
        .btn-submit { padding: 10px 25px; background: #27ae60; color: white; border: none; border-radius: 3px; cursor: pointer; font-weight: 600; font-size: 1em; }
        .btn-submit:hover { background: #219a52; }
        .btn-cancel { padding: 10px 25px; background: #95a5a6; color: white; border: none; border-radius: 3px; cursor: pointer; font-weight: 600; font-size: 1em; margin-left: 10px; }
        .success-message { display: none; background: #d4edda; color: #155724; padding: 12px; border-radius: 4px; margin-bottom: 15px; }
    </style>
</head>
<body>
    <div class="page-header">
        <h1>Add New Payor</h1>
    </div>

    <div class="form-container">
        <div id="successMessage" class="success-message">Payor added successfully!</div>

        <form id="addPayorForm">
            <div class="form-group">
                <label for="payorName">Payor Name *</label>
                <input type="text" id="payorName" name="payorName" placeholder="Enter payor name" />
            </div>

            <div class="form-group">
                <label for="payorCode">Payor Code *</label>
                <input type="text" id="payorCode" name="payorCode" placeholder="e.g., ACM001" />
            </div>

            <div class="form-group">
                <label for="contactEmail">Contact Email *</label>
                <input type="text" id="contactEmail" name="contactEmail" placeholder="email@example.com" />
            </div>

            <div class="form-group">
                <label for="phoneNumber">Phone Number</label>
                <input type="text" id="phoneNumber" name="phoneNumber" placeholder="(555) 123-4567" />
            </div>

            <div class="form-group">
                <label for="maxContractValue">Max Contract Value *</label>
                <input type="text" id="maxContractValue" name="maxContractValue" placeholder="e.g., 50000" />
            </div>

            <div class="form-group">
                <label for="effectiveDate">Effective Date *</label>
                <input type="date" id="effectiveDate" name="effectiveDate" />
            </div>

            <div class="form-group">
                <label for="status">Status *</label>
                <select id="status" name="status">
                    <option value="">-- Select Status --</option>
                    <option value="Active">Active</option>
                    <option value="Inactive">Inactive</option>
                </select>
            </div>

            <button type="submit" class="btn-submit">Add Payor</button>
            <button type="button" class="btn-cancel" onclick="window.location.href='/Pages/PayorList.aspx'">Cancel</button>
        </form>
    </div>

    <script type="text/javascript">
        $(document).ready(function () {

            // jQuery Validate — form validation rules
            $('#addPayorForm').validate({
                rules: {
                    payorName: {
                        required: true,
                        minlength: 3,
                        maxlength: 100
                    },
                    payorCode: {
                        required: true,
                        minlength: 3,
                        maxlength: 10,
                        pattern: /^[A-Z0-9]+$/   // alphanumeric uppercase only
                    },
                    contactEmail: {
                        required: true,
                        email: true              // built-in email validation
                    },
                    phoneNumber: {
                        phoneUS: true            // from additional-methods.js
                    },
                    maxContractValue: {
                        required: true,
                        number: true,            // must be numeric
                        min: 1,                  // minimum value
                        max: 10000000            // maximum value
                    },
                    effectiveDate: {
                        required: true,
                        date: true               // must be valid date
                    },
                    status: {
                        required: true           // dropdown must be selected
                    }
                },
                messages: {
                    payorName: {
                        required: "Payor name is required",
                        minlength: "Name must be at least 3 characters",
                        maxlength: "Name cannot exceed 100 characters"
                    },
                    payorCode: {
                        required: "Payor code is required",
                        minlength: "Code must be at least 3 characters",
                        maxlength: "Code cannot exceed 10 characters",
                        pattern: "Code must be uppercase letters and numbers only"
                    },
                    contactEmail: {
                        required: "Email is required",
                        email: "Please enter a valid email address"
                    },
                    phoneNumber: {
                        phoneUS: "Please enter a valid US phone number"
                    },
                    maxContractValue: {
                        required: "Contract value is required",
                        number: "Must be a valid number",
                        min: "Value must be at least 1",
                        max: "Value cannot exceed 10,000,000"
                    },
                    effectiveDate: {
                        required: "Effective date is required",
                        date: "Please enter a valid date"
                    },
                    status: {
                        required: "Please select a status"
                    }
                },
                errorClass: "error",
                errorElement: "label",
                submitHandler: function (form) {
                    // Form is valid — submit via AJAX
                    var formData = {
                        name: $('#payorName').val(),
                        code: $('#payorCode').val(),
                        email: $('#contactEmail').val(),
                        phone: $('#phoneNumber').val(),
                        maxContractValue: parseFloat($('#maxContractValue').val()),
                        effectiveDate: $('#effectiveDate').val(),
                        status: $('#status').val()
                    };

                    // Simulate AJAX submit to ASMX
                    $.ajax({
                        url: '/WebServices/PayorService.asmx/AddPayor',
                        type: 'POST',
                        contentType: 'application/json; charset=utf-8',
                        data: JSON.stringify(formData),
                        success: function () {
                            $('#successMessage').fadeIn();
                            form.reset();
                            setTimeout(function () { $('#successMessage').fadeOut(); }, 3000);
                        },
                        error: function () {
                            alert('Error adding payor. Please try again.');
                        }
                    });
                }
            });
        });
    </script>
</body>
</html>
