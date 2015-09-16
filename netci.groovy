// Import the utility functionality.

import jobs.generation.Utilities;
import jobs.generation.InternalUtilities;

def project = 'Microsoft/ConcordExtensibilitySamples'
// Define build strings
def debugBuildString = '''call "C:\\Program Files (x86)\\Microsoft Visual Studio 14.0\\Common7\\Tools\\VsDevCmd.bat"
msbuild Iris\\Iris.sln /p:Configuration=Debug&&msbuild HelloWorld\\Cpp\\HelloWorld.sln /p:Configuration=Debug&&msbuild HelloWorld\\Cs\\HelloWorld.sln /p:Configuration=Debug'''
def releaseBuildString = '''call "C:\\Program Files (x86)\\Microsoft Visual Studio 14.0\\Common7\\Tools\\VsDevCmd.bat"
msbuild Iris\\Iris.sln /p:Configuration=Release&&msbuild HelloWorld\\Cpp\\HelloWorld.sln /p:Configuration=Release&&msbuild HelloWorld\\Cs\\HelloWorld.sln /p:Configuration=Release'''

// Generate the builds for debug and release

def windowsDebugJob = job(Utilities.getFullJobName(project, 'windows_debug', false)) {
  label('windows')
  steps {
    batchFile(debugBuildString)
  }
}

def windowsReleaseJob = job(Utilities.getFullJobName(project, 'windows_release', false)) {
  label('windows')
  steps {
    batchFile(releaseBuildString)
  }
}
             
def windowsDebugPRJob = job(Utilities.getFullJobName(project, 'windows_debug', true)) {
  label('windows')
  steps {
    batchFile(debugBuildString)
  }
}

def windowsReleasePRJob = job(Utilities.getFullJobName(project, 'windows_release', true)) {
  label('windows')
  steps {
    batchFile(releaseBuildString)
  }
}


// Generate the root build flow job for commit

def rootBuildFlowCommitJob = buildFlowJob(Utilities.getFullJobName(project, '', false)) {
	configure {
        def buildNeedsWorkspace = it / 'buildNeedsWorkspace'
        buildNeedsWorkspace.setValue('true')
    }
}
              
def rootBuildFlowPRJob = buildFlowJob(Utilities.getFullJobName(project, '', true)) {
	configure {
        def buildNeedsWorkspace = it / 'buildNeedsWorkspace'
        buildNeedsWorkspace.setValue('true')
    }
}

Utilities.addBuildJobsInParallel(rootBuildFlowCommitJob, [windowsDebugJob, windowsReleaseJob])

[windowsDebugJob, windowsReleaseJob, rootBuildFlowCommitJob].each { newJob ->
  InternalUtilities.addPrivatePermissions(newJob)
  InternalUtilities.addPrivateScm(newJob, project)
  Utilities.addStandardOptions(newJob)
  Utilities.addStandardNonPRParameters(newJob)
}

// Add push triggers for the root build flow job

Utilities.addGithubPushTrigger(rootBuildFlowCommitJob)
              
// Finish off the PR jobs
              
Utilities.addBuildJobsInParallel(rootBuildFlowPRJob, [windowsDebugPRJob, windowsReleasePRJob])

[windowsDebugPRJob, windowsReleasePRJob, rootBuildFlowPRJob].each { newJob ->
  InternalUtilities.addPrivatePermissions(newJob)
  InternalUtilities.addPrivatePRTestSCM(newJob, project)
  Utilities.addStandardOptions(newJob)
  Utilities.addStandardPRParameters(newJob, project)
}

// Add push triggers for the root build flow job

Utilities.addGithubPRTrigger(rootBuildFlowPRJob)