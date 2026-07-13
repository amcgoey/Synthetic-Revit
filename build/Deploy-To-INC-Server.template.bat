REM Extra files like Workset XLS files
robocopy /mir "[LOCAL_OUTPUT_PATH]\Synthetic" "[UNC_SERVER_PATH]\Synthetic" /xf *.addin

REM Manifest .addin files
robocopy "[LOCAL_OUTPUT_PATH]\Manifest for Deploy" "[UNC_SERVER_PATH]"

REM Primary Addin files like DLLs
robocopy "[LOCAL_OUTPUT_PATH]\INC Settings" "[UNC_SERVER_PATH]"

PAUSE
