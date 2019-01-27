# Hyperparameter Optimizer

Based on ML.Net but can be used with any other type of a model which can be invoked from a dotnet core console app.

This solution has 3 projects:

- HpOptimizerCore - contains the logic for both client and server processes and is the heart of the optimization. Internally it uses classes from the Microsoft.ML.Sweeper namespace.

- HpOptServer - contains a console app that launches the server process. The server doles out hyperparameter values to client processes. The clients evaluate models with the supplied hyperparameters and feed back the results of the evalution metric (e.g. AUC, Accuracy, F1Score, etc.).

- HpOptModelRunner - contains a sample client console app and sample model. It references the HpOptimizerCore project for communication with the server. Make a copy of this project and modify the "Program.fs" file to plug in your model. The instructions are contained in this file. 

The client and server procesess communicate via named pipes. Mutiple client processes can be used to parallelize model evaluation. 

### Run.fsx Launcher
To launch both the server and client processes in a consistent manner use the Run.fsx script located in the HpOptModelRunner project.

The script will have to be modified as to the right paths for the server/client dll locations.




