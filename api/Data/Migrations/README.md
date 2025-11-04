# Data/Migrations

This folder contains deprecated controllers and components that are kept for historical purposes but are no longer actively used in the application.

## BackfillController

The `BackfillController` was moved to this folder on [current date] and is now disabled. It returns a 403 Forbidden response for all requests.

**Reason for deprecation:** The backfill functionality is no longer needed as the system has evolved to handle data processing differently.

**Original location:** `Controllers/BackfillController.cs`

**Current behavior:** Returns 403 Forbidden with a message indicating the endpoint is deprecated.

**Historical purpose:** Preserved for reference and potential future analysis of the backfill implementation.
