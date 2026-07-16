---
name: revit-debug-by-user
description: Procedure for opening Visual Studio and running the debugger to review the build in Revit. Use when launching a debug session.
---

# Revit User Debugging Playbook (`revit-debug-by-user`)

Use this playbook to automatically initiate a Revit debugging session in Visual Studio.

## 1. Automated Debug Launch

> [!IMPORTANT]
> Because the background AI agent runs in a non-interactive Windows session (Session 0), running the launch script via agent tools will spawn Visual Studio and Revit headlessly in the background. To see the graphical user interface, **the user must run these commands directly in their own interactive terminal**.

To open Visual Studio, select the correct project, and start debugging automatically, run one of the following commands in your local terminal:

* **Debug the latest supported version (from `revit-config.md`):**
  ```powershell
  powershell -File .agents/skills/revit-debug-by-user/scripts/start_debugging.ps1
  ```
* **Debug a specific target version (e.g., Revit 2024):**
  ```powershell
  powershell -File .agents/skills/revit-debug-by-user/scripts/start_debugging.ps1 -Version 2024
  ```

## 2. Interactive Debugging Steps

Once the script initiates the session:

1. **Verify Visual Studio Setup:**
   * Visual Studio will open (or attach to an existing running instance) and load `Synthetic.sln`.
   * The startup project will automatically change to the selected version (e.g., `Synthetic2026`).
   * The debugger will start immediately (F5), building the project, signing the DLL, and launching Revit.
2. **Review in Revit:**
   * Place breakpoints inside the desired source files (e.g. command `Execute` methods).
   * Open the target test model in Revit.
   * Run the command from the custom "Synthetic" ribbon tab.
   * Visual Studio will catch the execution path at your breakpoints.
3. **Closing:**
   * Visual Studio and Revit will remain open for your inspection until you close them.
