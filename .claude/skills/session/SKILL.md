---
name: session
description: Manage session workflow, including session file creation, task understanding, todos, and summaries
---

# Session Skill

## Purpose

This skill manages a structured workflow for each development session related to drawing tree.

## When to use

Use this skill when:

- Starting a new session
- Managing session-based tasks
- Recording structured summaries and todos

## Input

本session主要任务: {{主要任务描述}}

- {{用户输入的任务细节}}
- {{用户输入的任务细节}}
...

## General Knowledge

- 工程所有相关代码可以在 src/ 中找到
- "一句话中文" 意思为: 该文本内容是中文，并且用一句话进行总结
- "要点格式" 意思为: 使用中文并采用 markdown 的 bullet points 格式进行输出, 并且每个要点都应该尽量简短且为一句话

# Context (必须读取)

你必须在开始前阅读以下内容：

- 阅读 sessions 文件夹下的所有文件作为历史记忆
- 阅读 Coding Style Guide 以了解代码规范
- 阅读 Project Description 以了解项目背景和目标

## Behavior

The agent should:

1. Generate a clear understanding of the session task, using the following format:

- 简短任务总结（一句话中文）
- 理解与推断（要点格式）

2. Output the understanding (from step 1) and pause for user confirmation

3. 在用户确认正确后, 创建一个新的 session 文件在 `sessions/`，文件格式为 `session{number}.md`, 将第1步生成的内容填入文件中

4. Plan TODO steps based on the understanding, output the TODO steps, then pause for user confirmation

5. 在开始步骤后, for each TODO step:

- Execute the step
- Output the result
- Pause for user confirmation before proceeding to the next step
- 注意, 在此步骤结束以前, 不要进行任何下一步的操作, 包括但不限于: 代码修改, 文件创建, session文件创建等

6. 在本session结束后, 经过用户确认之后, 生成内容:

- Session内容总结（要点格式）
- 操作及决策细节（要点格式）

7. 将第6步生成的内容填入 session 文件中

## References

[Coding Style Guide](drawing_tree/.github/references/coding_style.md)
[Project Description](drawing_tree/.github/references/project_description.md)
[Icon Library](drawing_tree/public/icons)

## Template

[Summary Template](templates/summary_template.md)
