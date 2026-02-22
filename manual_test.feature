# Use placeholders so only test data (CSV/User Secrets) changes per environment.
# Required: Username, Password (or set TestData:CsvPath / TestSecrets in config).

Feature: Manual Supplier Workflow Test

  Scenario: Login and Navigate to Suppliers
    When I type '{{Username}}' into the 'Username' field
    And I type '{{Password}}' into the 'Password' field
    And I click the 'Sign In' button
    And I click the 'Accept All Cookies' button
    And I click the 'Accept' button on the 'Instructions' popup
    And I click the 'Products' menu item
    And I click the 'Suppliers' option
    Then I see the 'Suppliers List'
