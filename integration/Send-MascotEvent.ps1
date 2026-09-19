# Codex hook relay. Only event metadata is persisted; prompt/tool input is discarded.
param([switch]$MascotBridge, [string]$EventDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CodexMascot\events'))
$ErrorActionPreference = 'Stop'
try {
    $inputEvent = [Console]::In.ReadToEnd() | ConvertFrom-Json
    if (-not [string]::IsNullOrWhiteSpace($inputEvent.session_id)) {
        [void][IO.Directory]::CreateDirectory($EventDirectory)
        $eventRecord = [ordered]@{
            version = 1
            timestamp = [DateTimeOffset]::UtcNow.ToString('o')
            session_id = [string]$inputEvent.session_id
            turn_id = [string]$inputEvent.turn_id
            cwd = [string]$inputEvent.cwd
            hook_event_name = [string]$inputEvent.hook_event_name
            tool_name = [string]$inputEvent.tool_name
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
[Console]::Out.WriteLine('{}')
exit 0
