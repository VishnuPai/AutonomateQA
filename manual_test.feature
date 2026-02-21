Feature: Manual Supplier Workflow Test

  Scenario: Login and Navigate to Suppliers
    # 1. Login
    When I type 'myusername' into the 'Username' field
    And I type 'mypassword' into the 'Password' field
    And I click the 'Sign In' button
    
    # 2. Handle Popups (AI should detect them if they appear)
    # Using generic 'Accept' or 'Close' actions usually works for consent forms
    And I click the 'Accept All Cookies' button
    And I click the 'Close' button on the 'Instructions' popup 
    
    # 3. Navigation
    # Wait ensuring Home Page is ready (optional, but good practice)
    And I wait for the 'Home Page' to load
    
    # Navigate Menu
    And I click the 'Products' menu item
    And I click the 'Suppliers' option
    
    # 4. Verification
    Then I see the 'Suppliers List'
