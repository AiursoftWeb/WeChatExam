I have analyzed the current workspace `Aiursoft.WeChatExam` and cannot find any code related to the "Leave System" (请假系统) described in Issue #36.

Based on my investigation:
1.  **Missing Entities**: There are no entities for `Leave`, `Holiday`, or `Vacation`. The existing entities (`Exam`, `Paper`, `Question`, `User`) are strictly related to an exam system.
2.  **Missing Logic**: There is no logic for leave applications, approval workflows, or deducting leave days.
3.  **Context Mismatch**: My global memory indicates that the `Annual_Leave_Per_Year` setting and related leave management logic were implemented in the **`Aiursoft.EmployeeCenter`** project, not `Aiursoft.WeChatExam`.

It appears that Issue #36 belongs to the `Aiursoft.EmployeeCenter` project. Please switch to the correct workspace to resolve this issue.
