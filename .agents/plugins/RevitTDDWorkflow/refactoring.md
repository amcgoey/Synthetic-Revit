# **Refactoring**

## **The Golden Rule: Never Refactor on Red**

You are strictly forbidden from refactoring code while tests are failing. Refactoring is a privilege earned by getting to Green. If a test is failing, your only job is to write the minimal, code required to make it pass.

## **What is Refactoring?**

Refactoring changes **how** the code does something (its internal structure) without changing **what** it does (its observable behavior).

* Extracting a complex block of code into a private C# method.  
* Renaming variables, classes, or interfaces for clarity.  
* Consolidating duplicated logic.  
* Applying SOLID principles to decouple classes.

## **The Process**

1. Run the test suite. Ensure everything is **Green**.  
2. Make **one** small structural change.  
3. Run the test suite again.  
4. If it remains **Green**, commit the change (or proceed to the next small step).

## **Handling Failure During Refactoring**

If a test turns **Red** while you are refactoring:

* **DO NOT** fix the test to match your new code.  
* **DO NOT** write new production code to force the test to pass.  
* **DO** immediately revert your refactoring step (undo) to get back to Green.  
* Re-evaluate your refactoring strategy and take a smaller step.

### **The Exception: Bad Tests**

The only exception to the reversion rule is if you discover a test was poorly written and coupled to internal implementation details (e.g., asserting that a specific private method was called). In this case, the *test itself* is the problem. You must rewrite the test to focus on public observable behavior, verify it passes against the old code, and *then* resume your refactoring.
