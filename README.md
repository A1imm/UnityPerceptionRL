# Unity Perception RL

This project was developed as part of a master's thesis at the Silesian University of Technology. Its purpose is to compare two methods of environment perception used by reinforcement learning agents in procedurally generated Unity environments:

- **Raycast-based perception**, which provides the agent with structured information about its surroundings.
- **Camera-based perception**, in which the agent learns directly from visual observations.

Both agent variants are trained with equivalent actions, reward functions, and learning objectives. This makes it possible to focus the comparison on the influence of the observation method.

## Objective

- Design comparable agents using raycast and visual observations.
- Evaluate the agents in procedurally generated environments.
- Compare learning progress, sample efficiency, and final performance.
- Examine how task complexity affects the usefulness of each perception method.

## Environments

The project contains three types of tasks:

1. **Object Collection** – the agent must find and collect an object.
2. **Maze Navigation** – the agent must reach a goal while navigating a maze with obstacles.
3. **Combined Task** – the agent must collect an object and then reach the goal in a maze with obstacles.

Environment elements such as walls, platforms, obstacles, objects, and goal positions can be generated procedurally. This increases the variety of training and evaluation scenarios.

## Technical Stack

- **Engine**: Unity
- **Machine Learning Framework**: Unity ML-Agents Toolkit
- **Training Algorithm**: Proximal Policy Optimization (PPO)
- **Languages**: C#, Python
- **Training and Evaluation**: ML-Agents, TensorBoard, Python data-analysis tools
- **Platform**: Windows

The exact Unity and package versions used by the project are recorded in `ProjectSettings/ProjectVersion.txt` and `Packages/manifest.json`.

## How to Run

1. Clone the repository.
2. Add the project directory in Unity Hub.
3. Open it with the Unity version specified in `ProjectSettings/ProjectVersion.txt`.
4. Allow Unity to restore the packages listed in `Packages/manifest.json`.
5. Open the required training or evaluation scene.
6. To train an agent, activate a compatible Python environment and run:

   ```bash
   mlagents-learn path/to/config.yaml --run-id=<run-name>
   ```

7. When the message asking you to start the Unity environment appears, press **Play** in the Unity Editor.

The scene names and training configuration paths depend on the experiment being reproduced.

## Project Structure

```text
UnityPerceptionRL/
├── Assets/                 # Scenes, scripts, prefabs, models, and training configurations
├── Packages/               # Unity package manifest and lock file
├── ProjectSettings/        # Unity project configuration
├── ResearchResults/        # Training and evaluation data used in the analysis
│   ├── Evaluation/         # Evaluation results, including exported CSV files
│   └── Training/
│       ├── CSV/
│       │   ├── Processed/  # Processed data used to create plots
│       │   └── Raw/        # Raw training data for individual agents
│       └── TensorBoard/    # TensorBoard data grouped by agent and task
├── docs/                   # Images and recordings used in this README
│   ├── images/
│   └── videos/
├── .gitignore              # Files excluded from version control
└── README.md               # Project documentation
```

Unity-generated directories such as `Library`, `Logs`, `Temp`, and `UserSettings` are intentionally excluded from version control.

## Research Scope

The experiments compare the raycast-based and camera-based agents under matching conditions. The analysis focuses on metrics such as cumulative reward, task completion rate, training stability, and the number of training steps required to learn the task.

The repository contains the Unity project and selected research artifacts required to inspect the implemented environments and reproduce the experiments. Large raw training logs and intermediate checkpoints may be stored separately when they are not suitable for regular Git version control.

## Results

The experiments cover all three task types for both perception methods. Selected training and evaluation data are available in the `ResearchResults` directory.

<p align="center">
  <img src="docs/images/success-rate-comparison.png" alt="Success rate comparison between raycast-based and visual perception" width="850"/>
</p>

<p align="center">
  <em>Evaluation success rates for raycast-based and visual perception across the three task types.</em>
</p>

Both agents achieved a 100% success rate in the object collection task. The difference became more noticeable as task complexity increased. In the combined task, the raycast-based agent achieved a 95.2% success rate, while the visual agent achieved 67.6%.

## Demonstration

<p align="center">
  <a href="docs/videos/combined-task-demo.mp4">
    <img src="docs/images/combined-task-preview.png" alt="Combined task environment demonstration" width="600"/>
  </a>
</p>

<p align="center">
  <a href="docs/videos/combined-task-demo.mp4">▶ Watch the combined-task recording</a>
</p>

## Academic Context

This project was developed as part of a master's thesis at:

> **Silesian University of Technology**  
> Faculty of Automatic Control, Electronics and Computer Science

## Author

**mgr inż. Alan Pawleta**  
Silesian University of Technology  
Faculty of Automatic Control, Electronics and Computer Science
