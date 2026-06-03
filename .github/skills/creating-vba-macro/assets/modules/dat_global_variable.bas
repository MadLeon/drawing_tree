'/**
' * Global Variables Declaration Module (dat_global_variable.bas)
' * 
' * Description:
' *  - Central storage for application-wide global variables
' *  - Contains development mode flags and configuration settings
' *  - Should be imported first before other modules that depend on globals
' * 
' * Usage:
' *  - Modify DEBUG_MODE to enable/disable development logging
' *  - Modify DISABLE_LOGGER to completely disable all logger functions
' *  - Add more global variables as needed for application state
' */

Option Explicit

' ============================================================================
' DEVELOPMENT MODE - Logger Configuration
' ============================================================================

' /**
'  * DEBUG_MODE: Controls whether development logging is active
'  * 
'  * Set to True to:
'  *   - Enable detailed logging via LogDebug()
'  *   - Log all operation steps for troubleshooting
'  *   - View comprehensive error messages
'  * 
'  * Set to False to:
'  *   - Disable debug-level logs in production
'  *   - Only log errors and important events
'  *   - Reduce log file size
'  * 
'  * Usage in code:
'  *   If DEBUG_MODE Then
'  *       LogDebug "Detailed operation info: " & someValue
'  *   End If
'  */
Public Const DEBUG_MODE As Boolean = True

' /**
'  * DISABLE_LOGGER: Completely disable all logger functionality
'  * 
'  * Set to True to:
'  *   - Disable all logging (LogDebug, LogError, LogInfo, etc.)
'  *   - Prevent any log file writes
'  *   - Skip all logger operations entirely
'  *   - No code changes needed in caller functions
'  * 
'  * Set to False to:
'  *   - Enable normal logger operation
'  *   - Write logs as configured
'  * 
'  * Note:
'  *   - All logger functions check this flag at startup
'  *   - When True, all logging functions return immediately without action
'  *   - Useful for production environments where logging is not needed
'  * 
'  * Usage in code:
'  *   - No need to check this flag explicitly
'  *   - Logger module handles it automatically
'  */
Public Const DISABLE_LOGGER As Boolean = False