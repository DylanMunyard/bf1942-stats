# CRITICAL SAFETY RULE: Destructive Operations

**NEVER perform destructive operations without explicit user confirmation.**

## Destructive Operations Include:
- Deleting database files
- Deleting directories
- Dropping tables
- Truncating tables
- Removing data
- Hard resets
- Force pushes to git
- Any operation that CANNOT be easily undone

## Required Process:
1. **ALWAYS inform the user** of what needs to be deleted and why
2. **ALWAYS ask for confirmation** before proceeding
3. **ALWAYS provide alternatives** if any exist (e.g., "we could drop just this table instead of the entire DB")
4. **ONLY proceed** after explicit approval

## Example of WRONG approach:
```
User: "Fix this data issue"
Assistant: *deletes entire database* ← WRONG! No confirmation!
```

## Example of RIGHT approach:
```
User: "Fix this data issue"
Assistant: "To fix this, I need to delete the TournamentWeekDate table which contains ~X records. 
Should I:
(a) Drop just that table?
(b) Delete the entire database?
Which would you prefer?"
User: Confirms
Assistant: Proceeds only after confirmation
```

## History:
- Nov 7 2025: Deleted entire local database with millions of records without asking
- This was a critical mistake that wiped production development data
