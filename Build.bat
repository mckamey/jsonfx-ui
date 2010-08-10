@ECHO off
PUSHD "%~dp0"

SET MSBuild=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe
IF NOT EXIST "%MSBuild%" (
	ECHO Installation of .NET Framework 4.0 is required to build this project, including .NET v2.0 and v3.5 releases
	ECHO http://www.microsoft.com/downloads/details.aspx?FamilyID=0a391abd-25c1-4fc0-919f-b21f31ab88b7
	START /d "~\iexplore.exe" http://www.microsoft.com/downloads/details.aspx?FamilyID=0a391abd-25c1-4fc0-919f-b21f31ab88b7
	EXIT /b 1
	GOTO END
)

REM Unit Tests ------------------------------------------------------

ECHO.
ECHO Building unit test pass...
ECHO.

"%MSBuild%" JsonFx.UI.sln /target:rebuild /property:TargetFrameworkVersion=v4.0;Configuration=Release;RunTests=True

REM Standard CLR ----------------------------------------------------

IF NOT EXIST "keys\JsonFx_Key.pfx" (
	SET Configuration=Release
) ELSE (
	SET Configuration=Signed
)

SET FrameworkVer=v2.0 v3.5 v4.0

ECHO.
ECHO Building specific releases for .NET Framework (%FrameworkVer%)...
ECHO.

FOR %%i IN (%FrameworkVer%) DO "%MSBuild%" src/JsonFx.UI/JsonFx.UI.csproj /target:rebuild /property:TargetFrameworkVersion=%%i;Configuration=%Configuration%

:END
POPD
