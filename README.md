Unity Optix Plugin by Alex Scott

Usage Instructions:
Requirements:
- Unity 5 or greater.

1. Copy the contents from the UnityOptixPluginCompiled folder to the Plugins directory of your project.

2. Copy all the .cs files from the UnityOptixCSharp folder to somewhere in your project. Or optionally open the csproj file
   in Visual Studio and change the output directory to somewhere in your project. See compilation instructions (11).
   
3. Copy all the contents from the UnityOptixPluginMaterials folder to somewhere in your project.
   
4. In Unity add the OptixSensor component to a gameobject. Also add the OptixExampleUsage component to an empty  
   gameobject to see an example of it's usage.
   
5. Add the following references to the OptixExampleUsage component:
	- Create a UI button and assign it's reference to the Sensor Trigger Button variable.
	- Set the Instance Mesh variable to whatever mesh you want the point cloud point mesh to be. The sphere primitive is recommended.
	- Set the Instance Material variable to PointCloudPointMat that was copied from the UnityOptixPluginMaterials folder.
	- Set the Optix Target Layer Mask to a specific layer that the detectable objects are on or simply set it to everything.

6. Run the application and click on the UI button. If anything is infront of your OptixSensor it will be rendered into a point cloud.


Compilation instructions:
Requirements:
- Visual Studio 2015 with C++ compiler.
- CMake 3.0 or greater.
- NVIDIA Optix 4.1 or greater.
- CUDA Toolkit 5.0 or greater.

1. Copy the UnityOptixPlugin folder to the C:\ProgramData\NVIDIA Corporation\OptiX SDK <version>\SDK directory.

2. Copy the CMakeLists.txt (root one not the one in the UnityOptixPlugin folder) file to the 
   C:\ProgramData\NVIDIA Corporation\OptiX SDK <version>\SDK directory and overwrite the file there.

3. Start up cmake-gui from the Start Menu.

4. Select the C:\ProgramData\NVIDIA Corporation\OptiX SDK <version>\SDK directory
   from the installation for the source file location.

5. Create a build directory that isn't the same as the source directory.  For
   example, C:\ProgramData\NVIDIA Corporation\OptiX SDK <version>\SDK\build.
   If you don't have permissions to write into the this directory (writing into
   the "C:/Program Files" directory can be restricted in some cases), pick a different
   directory where you do have write permissions.  If you type in the directory
   (instead of using the "Browse Build..." button), CMake will ask you at the
   next step to create the directory for you if it doesn't already exist.

6. Press "Configure" button and select the version of Visual Studio you wish to
   use.  Note that the 64-bit compiles are separate from the 32-bit compiles
   (e.g. look for "Visual Studio 12 2013 Win64").  Leave all other options on
   their default.  Press "OK".  This can take a while while source level
   dependencies for CUDA files are computed.

7. Press "Configure" again.  Followed by "Generate".

8. Open the Unity-Optix-Plugin.sln solution file in the build directory you created.

9. Build the solution. There will be errors.

10. Open the properties windows on the unityOptixPlugin project and go to Configuration Properties -> C/C++ -> Preprocesor -> Preprocessor Definitions.
Add to the list of definitions: OPTIXPLUGIN_EXPORTS.

11. Still in the unityOptixPlugin properties, go to Configuration Properties -> General. Change Target Extension to .dll. Change configuration type
to .dll. Change the output directory to the plugins directory of your Unity project.

12. Repeat step 12 for the sutil_sdk project.

13. Build the solution again and you should be error free.