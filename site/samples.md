# Samples

Every file here lives in [`samples/`](https://github.com/fgilde/QuickRun/tree/main/samples) and is
validated in CI against all three platforms, so nothing on this page can drift from the engine.

## Smallest useful config

<<< @/../samples/npm-dev.yml{yaml}

## .NET web application

<<< @/../samples/dotnet-web.yml{yaml}

## Python with a virtual environment

Shows how platform maps deal with the venv layout difference between Windows and everything else.

<<< @/../samples/python-venv.yml{yaml}

## Several services at once

Postgres, a .NET API and a Vite front end, started in dependency order and cleaned up on stop.

<<< @/../samples/multi-service.yml{yaml}

## Wrapping an existing compose file

<<< @/../samples/docker-compose.yml{yaml}

## A generated input form

Required secret with a validated pattern, a number with a range, a dropdown and a switch.

<<< @/../samples/inputs-and-secrets.yml{yaml}

## One script per platform

<<< @/../samples/platform-scripts.yml{yaml}

## A repository that brings its own SDK

Nothing to `require`, because the setup installs what it needs.

<<< @/../samples/install-dotnet-then-run.yml{yaml}

## QuickRun's own config

QuickRun runs itself; this is what the extension button does on its repository.

<<< @/../quickrun.yml{yaml}
