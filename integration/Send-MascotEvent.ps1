# Codex hook relay. Only event metadata is persisted; prompt/tool input is discarded.
param([switch]$MascotBridge, [switch]$AntigravityStop, [switch]$QuietOutput, [string]$EventDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexMascot\events'), [string]$HookEventName)
$ErrorActionPreference = 'Stop'
try {
    $inputEvent = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $sessionId = if ($inputEvent.session_id) { [string]$inputEvent.session_id } elseif ($inputEvent.sessionId) { [string]$inputEvent.sessionId } elseif ($inputEvent.conversationId) { [string]$inputEvent.conversationId } elseif ($inputEvent.trajectory_id) { [string]$inputEvent.trajectory_id } else { [string]$inputEvent.conversation_id }
    $projectPath = if ($inputEvent.cwd) { [string]$inputEvent.cwd } elseif ($inputEvent.workspacePaths) { [string]@($inputEvent.workspacePaths)[0] } elseif ($inputEvent.workspace_roots) { [string]@($inputEvent.workspace_roots)[0] } elseif ($inputEvent.agent_action_name) { [string](Get-Location).Path } else { '' }
    $eventName = if ($HookEventName) { $HookEventName } elseif ($inputEvent.hook_event_name) { [string]$inputEvent.hook_event_name } else { [string]$inputEvent.agent_action_name }
    $isFirstInvocation = $eventName -ne 'PreInvocation' -or [string]$inputEvent.invocationNum -eq '0'
    $isFullyIdle = $eventName -ne 'Stop' -or $null -eq $inputEvent.fullyIdle -or [bool]$inputEvent.fullyIdle
    if (-not [string]::IsNullOrWhiteSpace($sessionId) -and $isFirstInvocation -and $isFullyIdle) {
        [void][IO.Directory]::CreateDirectory($EventDirectory)
        $eventRecord = [ordered]@{
            version = 1
            timestamp = [DateTimeOffset]::UtcNow.ToString('o')
            session_id = $sessionId
            turn_id = if ($inputEvent.turn_id) { [string]$inputEvent.turn_id } elseif ($inputEvent.execution_id) { [string]$inputEvent.execution_id } elseif ($inputEvent.generation_id) { [string]$inputEvent.generation_id } else { $null }
            cwd = $projectPath
            hook_event_name = $eventName
            tool_name = if ($inputEvent.tool_name) { [string]$inputEvent.tool_name } else { [string]$inputEvent.toolName }
            notification_type = [string]$inputEvent.notification_type
            termination_reason = [string]$inputEvent.terminationReason
        }
        $name = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffff') + '-' + [Guid]::NewGuid().ToString('N')
        $tempPath = Join-Path $EventDirectory ($name + '.tmp')
        $destinationPath = Join-Path $EventDirectory ($name + '.json')
        [IO.File]::WriteAllText($tempPath, ($eventRecord | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($tempPath, $destinationPath)
    }
} catch {
    # A mascot failure must never block the user's Codex work.
}
if ($QuietOutput) {
    # Kiro feeds hook stdout into the agent context; metadata-only hooks stay silent.
} elseif ($HookEventName -eq 'beforeSubmitPrompt') {
    [Console]::Out.WriteLine('{"continue":true}')
} elseif ($AntigravityStop) {
    [Console]::Out.WriteLine('{"decision":"allow"}')
} else {
    [Console]::Out.WriteLine('{}')
}
exit 0
