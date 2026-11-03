# LaunchPad - drag things in, save one Desktop shortcut (with your own icon) that opens them all at once.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

Add-Type -Namespace LP -Name Native -MemberDefinition @'
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct SHFILEINFO {
    public IntPtr hIcon; public int iIcon; public uint dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
}
[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
public static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
public static extern int PickIconDlg(IntPtr hwnd, System.Text.StringBuilder path, uint cch, ref int index);
[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
public static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint n);
[DllImport("user32.dll")]
public static extern bool DestroyIcon(IntPtr h);
[DllImport("shell32.dll", EntryPoint = "#727")]
public static extern int SHGetImageList(int list, ref Guid riid, out IntPtr ppv);
[DllImport("comctl32.dll")]
public static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, uint flags);
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint n, uint flags);
[DllImport("dwmapi.dll")]
public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
'@

$LaunchDir = Join-Path $env:LOCALAPPDATA 'LaunchPad\Launchers'
$Fallback  = "$env:SystemRoot\System32\shell32.dll,3"
$ws = New-Object -ComObject WScript.Shell

function Get-ShellIcon([string]$p) {
    # Real Explorer icon for a file/shortcut/folder; browser icon for web links
    $info = New-Object LP.Native+SHFILEINFO
    $flags = 0x100  # SHGFI_ICON | LARGEICON
    if ($p -match '^[a-z][a-z0-9+.-]*://') { $p = '.html'; $flags = $flags -bor 0x10 }  # USEFILEATTRIBUTES
    [void][LP.Native]::SHGetFileInfo($p, 0x80, [ref]$info, [System.Runtime.InteropServices.Marshal]::SizeOf($info), $flags)
    if ($info.hIcon -eq [IntPtr]::Zero) { return $null }
    $bmp = ([System.Drawing.Icon]::FromHandle($info.hIcon)).ToBitmap()
    [void][LP.Native]::DestroyIcon($info.hIcon)
    $bmp
}

function Get-IconFromLoc([string]$loc) {
    # "file,index" -> bitmap (for the preview box)
    $i = $loc.LastIndexOf(',')
    $file = [Environment]::ExpandEnvironmentVariables($loc.Substring(0, $i)); $idx = [int]$loc.Substring($i + 1)
    $big = New-Object IntPtr[] 1
    if ([LP.Native]::ExtractIconEx($file, $idx, $big, $null, 1) -gt 0 -and $big[0] -ne [IntPtr]::Zero) {
        $bmp = ([System.Drawing.Icon]::FromHandle($big[0])).ToBitmap(); [void][LP.Native]::DestroyIcon($big[0]); return $bmp
    }
    $null
}

function Save-JumboIcon([string]$p) {
    # Grab Explorer's 256px icon for $p and save it as a .ico, return "file,0"
    try {
        $dir = Join-Path $env:LOCALAPPDATA 'LaunchPad\Icons'
        [void](New-Item -ItemType Directory -Force $dir)
        $md5 = [System.Security.Cryptography.MD5]::Create()
        $hash = -join ($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($p.ToLower())) | ForEach-Object { $_.ToString('x2') })
        $out = Join-Path $dir "$hash.ico"
        if (Test-Path $out) { return "$out,0" }
        $info = New-Object LP.Native+SHFILEINFO
        [void][LP.Native]::SHGetFileInfo($p, 0, [ref]$info, [System.Runtime.InteropServices.Marshal]::SizeOf($info), 0x4000)  # SYSICONINDEX
        $iid = [Guid]'46EB5926-582E-4017-9FDF-E8998DAA0950'
        $himl = [IntPtr]::Zero
        if ([LP.Native]::SHGetImageList(4, [ref]$iid, [ref]$himl) -ne 0) { return $null }  # 4 = JUMBO 256px
        $h = [LP.Native]::ImageList_GetIcon($himl, $info.iIcon, 1)
        if ($h -eq [IntPtr]::Zero) { return $null }
        $bmp = ([System.Drawing.Icon]::FromHandle($h)).ToBitmap(); [void][LP.Native]::DestroyIcon($h)
        $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $png = $ms.ToArray(); $bmp.Dispose()
        $fs = [System.IO.File]::Create($out); $bw = New-Object System.IO.BinaryWriter($fs)
        $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)                  # ICO header, 1 image
        $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0)    # 256x256, no palette
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$png.Length); $bw.Write([uint32]22)
        $bw.Write($png); $bw.Close()
        "$out,0"
    } catch { $null }
}

function Write-Ico([System.Drawing.Bitmap]$src, [string]$out) {
    # Multi-size .ico (256/64/48/32/16, PNG inside) so it looks sharp at every Desktop size
    $sizes = 256, 64, 48, 32, 16
    $pngs = foreach ($s in $sizes) {
        $b = New-Object System.Drawing.Bitmap($s, $s)
        $g = [System.Drawing.Graphics]::FromImage($b)
        $g.InterpolationMode = 'HighQualityBicubic'; $g.DrawImage($src, 0, 0, $s, $s); $g.Dispose()
        $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
        , $ms.ToArray()
    }
    $fs = [System.IO.File]::Create($out); $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $off = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i] % 256
        $bw.Write([byte]$s); $bw.Write([byte]$s); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$off)
        $off += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Close()
}

function Export-IconLoc([string]$loc, [string]$out) {
    # "file,index" -> sharp .ico file at $out
    $i = $loc.LastIndexOf(',')
    $file = [Environment]::ExpandEnvironmentVariables($loc.Substring(0, $i)); $idx = [int]$loc.Substring($i + 1)
    $h = New-Object IntPtr[] 1; $ids = New-Object int[] 1
    if ([LP.Native]::PrivateExtractIcons($file, $idx, 256, 256, $h, $ids, 1, 0) -lt 1 -or $h[0] -eq [IntPtr]::Zero) { return $false }
    $bmp = ([System.Drawing.Icon]::FromHandle($h[0])).ToBitmap(); [void][LP.Native]::DestroyIcon($h[0])
    Write-Ico $bmp $out; $bmp.Dispose(); $true
}

$Csc = "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$Marker = "`n#LAUNCHPAD-DATA#`n"

function Build-Launcher([string]$exe, [string[]]$items, [string[]]$names, [string]$iconLoc, [bool]$custom) {
    # Compiles a tiny standalone .exe that opens every item. Returns $null or an error message.
    # Build inside %LOCALAPPDATA%\LaunchPad so one antivirus allow-list folder covers it
    $tmp = Join-Path $env:LOCALAPPDATA ("LaunchPad\build\" + [guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Force $tmp)
    try {
        $q = { param($s) '@"' + ($s -replace '"', '""') + '"' }
        $title = ($names -join ', ')
        if ($title.Length -gt 250) { $title = $title.Substring(0, 247) + '...' }
        # The list lives in the normal file info (Comments) so LaunchPad can open it again later
        $data = @{ items = $items; icon = $iconLoc; custom = $custom } | ConvertTo-Json -Compress
        $src = @"
using System; using System.Diagnostics; using System.Reflection; using System.Windows.Forms;
[assembly: AssemblyTitle($(& $q "Opens: $title"))]
[assembly: AssemblyDescription($(& $q $data))]
[assembly: AssemblyProduct("LaunchPad launcher")]
[assembly: AssemblyVersion("1.0.0.0")]
static class P {
    [STAThread] static void Main() {
        string[] items = { $(($items | ForEach-Object { & $q $_ }) -join ', ') };
        string bad = "";
        foreach (string i in items) {
            try { Process.Start(new ProcessStartInfo(i) { UseShellExecute = true }); } catch { bad += "\n" + i; }
        }
        if (bad != "") MessageBox.Show("Couldn't open:" + bad, "LaunchPad");
    }
}
"@
        [IO.File]::WriteAllText("$tmp\l.cs", $src, [Text.Encoding]::UTF8)
        $iconArg = @()
        if (Export-IconLoc $iconLoc "$tmp\l.ico") { $iconArg = @("/win32icon:$tmp\l.ico") }
        $outTmp = "$tmp\l.exe"
        $log = & $Csc /nologo /target:winexe /optimize+ "/out:$outTmp" @iconArg "$tmp\l.cs" 2>&1
        if (-not (Test-Path $outTmp)) { return "Build failed:`n$($log -join "`n")" }
        Copy-Item $outTmp $exe -Force
        $null
    } catch { "Couldn't save: $_" } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}

function Read-Launcher([string]$exe) {
    $c = (Get-Item -LiteralPath $exe).VersionInfo.Comments
    if ($c -and $c.StartsWith('{')) { try { return $c | ConvertFrom-Json } catch {} }
    $text = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($exe))   # older launchers kept it at the end
    $k = $text.LastIndexOf($Marker)
    if ($k -lt 0) { return $null }
    $text.Substring($k + $Marker.Length) | ConvertFrom-Json
}

function Get-Shorthand {
    $parts = foreach ($it in $list.Items) {
        $n = ($it.Text -replace '^https?://(www\.)?', '') -replace '[\\/:*?"<>|]', ''
        $n.Substring(0, [Math]::Min(3, $n.Length))
    }
    ($parts | Where-Object { $_ }) -join [char]0x00B7
}

function Get-DefaultIconLoc([string]$p) {
    # Best guess at a "file,index" icon for one item, or $null
    if ($p -like '*.lnk' -and (Test-Path -LiteralPath $p)) {
        $s = $ws.CreateShortcut($p)
        $f = ($s.IconLocation -split ',')[0]
        if ($f) { return $s.IconLocation }
        if ($s.TargetPath -like '*.exe') { return "$($s.TargetPath),0" }
        return Save-JumboIcon $p   # Store/installer shortcuts (Firefox, Signal...) have no icon file
    }
    if ($p -match '\.(exe|ico|dll)$' -and (Test-Path -LiteralPath $p)) { return "$p,0" }
    $null
}

# ---------- window ----------
$form = New-Object System.Windows.Forms.Form
$form.Text = 'LaunchPad'
$form.Size = New-Object System.Drawing.Size(680, 520)
$form.MinimumSize = New-Object System.Drawing.Size(520, 360)
$form.StartPosition = 'CenterScreen'
$form.AllowDrop = $true
$form.Font = New-Object System.Drawing.Font('Segoe UI', 10)
$appIcon = Join-Path $PSScriptRoot 'LaunchPad.ico'
if (Test-Path $appIcon) { $form.Icon = New-Object System.Drawing.Icon($appIcon) }

$hint = New-Object System.Windows.Forms.Label
$hint.Text = 'Drag icons, files, folders, shortcuts or web links here. Or paste a link below.'
$hint.Dock = 'Top'; $hint.Height = 30; $hint.Padding = New-Object System.Windows.Forms.Padding(8, 8, 8, 0)

$images = New-Object System.Windows.Forms.ImageList
$images.ImageSize = New-Object System.Drawing.Size(32, 32); $images.ColorDepth = 'Depth32Bit'

$list = New-Object System.Windows.Forms.ListView
$list.Dock = 'Fill'; $list.AllowDrop = $true; $list.View = 'Details'; $list.FullRowSelect = $true
$list.HeaderStyle = 'None'; $list.SmallImageList = $images
[void]$list.Columns.Add('Name', 230); [void]$list.Columns.Add('Path', 400)

$linkRow = New-Object System.Windows.Forms.Panel
$linkRow.Dock = 'Bottom'; $linkRow.Height = 38; $linkRow.Padding = New-Object System.Windows.Forms.Padding(6)
$linkBox = New-Object System.Windows.Forms.TextBox
$linkBox.Dock = 'Fill'; $linkBox.PlaceholderText = 'https://... or a program path, then Add'
$addBtn = New-Object System.Windows.Forms.Button
$addBtn.Text = 'Add'; $addBtn.Dock = 'Right'; $addBtn.Width = 70
$linkRow.Controls.AddRange(@($linkBox, $addBtn))

$btnRow = New-Object System.Windows.Forms.FlowLayoutPanel
$btnRow.Dock = 'Bottom'; $btnRow.Height = 50; $btnRow.Padding = New-Object System.Windows.Forms.Padding(4)
function New-Btn($text, $w = 80) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $text; $b.Width = $w; $b.Height = 36
    $btnRow.Controls.Add($b); $b
}
$upBtn     = New-Btn 'Up' 50
$downBtn   = New-Btn 'Down' 60
$removeBtn = New-Btn 'Remove'
$testBtn   = New-Btn 'Test run' 85
$openBtn   = New-Btn 'Open' 65
$iconBox = New-Object System.Windows.Forms.PictureBox
$iconBox.Size = New-Object System.Drawing.Size(36, 36); $iconBox.SizeMode = 'CenterImage'
$iconBox.Margin = New-Object System.Windows.Forms.Padding(14, 3, 2, 3)
$btnRow.Controls.Add($iconBox)
$iconBtn   = New-Btn 'Icon...' 75
$saveBtn   = New-Btn 'Save' 80

$form.Controls.AddRange(@($list, $hint, $linkRow, $btnRow))

# ---------- state ----------
$script:IconLoc = $null   # chosen "file,index"; $null = automatic (first item's icon)

function Get-CurrentIconLoc {
    if ($script:IconLoc) { return $script:IconLoc }
    foreach ($it in $list.Items) { $l = Get-DefaultIconLoc $it.Tag; if ($l) { return $l } }
    $Fallback
}
function Update-IconPreview {
    $old = $iconBox.Image
    $iconBox.Image = Get-IconFromLoc (Get-CurrentIconLoc)
    if ($old) { $old.Dispose() }
}

function Add-Item([string]$p) {
    $p = $p.Trim().Trim('"')
    if (-not $p) { return }
    foreach ($it in $list.Items) { if ($it.Tag -eq $p) { return } }
    $name = if ($p -match '://') { $p } else { [System.IO.Path]::GetFileNameWithoutExtension($p.TrimEnd('\')) }
    if (-not $name) { $name = $p }
    $key = "k$($images.Images.Count)"
    $bmp = Get-ShellIcon $p
    if ($bmp) { $images.Images.Add($key, $bmp) }
    $item = New-Object System.Windows.Forms.ListViewItem($name, $key)
    [void]$item.SubItems.Add($p); $item.Tag = $p
    [void]$list.Items.Add($item)
    Update-IconPreview
}

$onDragEnter = {
    param($s, $e)
    if ($e.Data.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop) -or
        $e.Data.GetDataPresent([System.Windows.Forms.DataFormats]::Text)) { $e.Effect = 'Copy' }
}
$onDrop = {
    param($s, $e)
    if ($e.Data.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop)) {
        foreach ($f in $e.Data.GetData([System.Windows.Forms.DataFormats]::FileDrop)) { Add-Item $f }
    } elseif ($e.Data.GetDataPresent([System.Windows.Forms.DataFormats]::Text)) {
        foreach ($l in ($e.Data.GetData([System.Windows.Forms.DataFormats]::Text) -split "`r?`n")) { Add-Item $l }
    }
}
foreach ($c in @($form, $list)) { $c.Add_DragEnter($onDragEnter); $c.Add_DragDrop($onDrop) }

$addBtn.Add_Click({ Add-Item $linkBox.Text; $linkBox.Clear() })
$linkBox.Add_KeyDown({ param($s, $e) if ($e.KeyCode -eq 'Enter') { $addBtn.PerformClick(); $e.SuppressKeyPress = $true } })

$removeBtn.Add_Click({
    foreach ($i in @($list.SelectedItems)) { $list.Items.Remove($i) }
    if ($list.Items.Count -eq 0) { $script:LastSaved = $null; $script:IconLoc = $null }  # empty list = start fresh
    Update-IconPreview
})
$list.Add_KeyDown({ param($s, $e) if ($e.KeyCode -eq 'Delete') { $removeBtn.PerformClick() } })

function Move-Sel([int]$d) {
    if ($list.SelectedItems.Count -ne 1) { return }
    $item = $list.SelectedItems[0]; $i = $item.Index; $j = $i + $d
    if ($j -lt 0 -or $j -ge $list.Items.Count) { return }
    $list.Items.RemoveAt($i); $list.Items.Insert($j, $item)
    $item.Selected = $true; $item.Focused = $true; $list.Focus()
    Update-IconPreview
}
$upBtn.Add_Click({ Move-Sel -1 })
$downBtn.Add_Click({ Move-Sel 1 })

$testBtn.Add_Click({
    $sh = New-Object -ComObject Shell.Application
    foreach ($i in $list.Items) { $sh.ShellExecute($i.Tag) }
})

$iconBtn.Add_Click({
    $loc = Get-CurrentIconLoc
    $k = $loc.LastIndexOf(',')
    $sb = New-Object System.Text.StringBuilder(1024)
    [void]$sb.Append($loc.Substring(0, $k)); $idx = [int]$loc.Substring($k + 1)
    if ([LP.Native]::PickIconDlg($form.Handle, $sb, 1024, [ref]$idx) -ne 0) {
        $script:IconLoc = "$([Environment]::ExpandEnvironmentVariables($sb.ToString())),$idx"
        Update-IconPreview
    }
})

$saveBtn.Add_Click({
    if ($list.Items.Count -eq 0) { [System.Windows.Forms.MessageBox]::Show('Nothing to save yet. Drag some things in first.', 'LaunchPad') | Out-Null; return }
    $dlg = New-Object System.Windows.Forms.SaveFileDialog
    # You pick the shortcut's name/place (Desktop); the .exe itself lives in the allow-listed Launchers folder
    $dlg.Filter = 'Desktop shortcut (*.lnk)|*.lnk'
    $dlg.InitialDirectory = if ($script:LastSaved) { Split-Path $script:LastSaved } else { [Environment]::GetFolderPath('Desktop') }
    $dlg.FileName = if ($script:LastSaved) { Split-Path $script:LastSaved -Leaf } else { (Get-Shorthand) + '.lnk' }
    if ($dlg.ShowDialog() -ne 'OK') { return }
    $form.Cursor = 'WaitCursor'
    [void](New-Item -ItemType Directory -Force $LaunchDir)
    $exe = Join-Path $LaunchDir ([IO.Path]::GetFileNameWithoutExtension($dlg.FileName) + '.exe')
    $items = @($list.Items | ForEach-Object { [string]$_.Tag })
    $names = @($list.Items | ForEach-Object { $_.Text })
    $err = Build-Launcher $exe $items $names (Get-CurrentIconLoc) ([bool]$script:IconLoc)
    $form.Cursor = 'Default'
    if ($err) { [System.Windows.Forms.MessageBox]::Show($err, 'LaunchPad') | Out-Null; return }
    $s = $ws.CreateShortcut($dlg.FileName)
    $s.TargetPath = $exe; $s.WorkingDirectory = $LaunchDir
    $s.IconLocation = "$exe,0"
    $s.Description = 'Opens: ' + ($names -join ', ')   # the hover text on the Desktop
    $s.Save()
    $script:LastSaved = $dlg.FileName
    [System.Windows.Forms.MessageBox]::Show("Saved:`n$($dlg.FileName)`n`nDouble-click it to open everything at once.", 'LaunchPad') | Out-Null
})

$openBtn.Add_Click({
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Filter = 'Launcher (*.exe;*.lnk;*.vbs;*.bat)|*.exe;*.lnk;*.vbs;*.bat'
    $dlg.InitialDirectory = [Environment]::GetFolderPath('Desktop')
    $dlg.DereferenceLinks = $false
    if ($dlg.ShowDialog() -ne 'OK') { return }
    $file = $dlg.FileName; $script:IconLoc = $null; $script:LastSaved = $null
    $picked = $file
    if ($file -like '*.lnk' -and $ws.CreateShortcut($file).TargetPath -like '*.exe') { $file = $ws.CreateShortcut($file).TargetPath }
    if ($file -like '*.exe') {
        $data = Read-Launcher $file
        if (-not $data) { [System.Windows.Forms.MessageBox]::Show("That program wasn't made by LaunchPad.", 'LaunchPad') | Out-Null; return }
        $list.Items.Clear()
        foreach ($i in $data.items) { Add-Item $i }
        if ($data.custom) { $script:IconLoc = $data.icon }
        $script:LastSaved = if ($picked -like '*.lnk') { $picked } else { Join-Path ([Environment]::GetFolderPath('Desktop')) ([IO.Path]::GetFileNameWithoutExtension($file) + '.lnk') }
        Update-IconPreview; return
    }
    if ($file -like '*.lnk') {
        $s = $ws.CreateShortcut($file)
        if ($s.Arguments -notmatch '"([^"]+\.vbs)"') { [System.Windows.Forms.MessageBox]::Show("That shortcut wasn't made by LaunchPad.", 'LaunchPad') | Out-Null; return }
        $file = $matches[1]
        if ($s.Description -like '*`[custom icon`]*') { $script:IconLoc = $s.IconLocation }
    }
    $list.Items.Clear()
    foreach ($l in [System.IO.File]::ReadAllLines($file)) {
        if ($l -match '^sh\.ShellExecute\s+"(.*)"\s*$') { Add-Item ($matches[1] -replace '""', '"') }
        elseif ($l -match '^start\s+""\s+"(.*)"\s*$') { Add-Item ($matches[1] -replace '%%', '%') }
    }
    Update-IconPreview
})

# ---------- light / dark ----------
$ThemeFile = Join-Path $env:LOCALAPPDATA 'LaunchPad\theme.txt'
$script:Dark = try { [string](Get-Content $ThemeFile -ErrorAction Stop) -eq 'dark' } catch {
    (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction SilentlyContinue).AppsUseLightTheme -eq 0
}
$themeBtn = New-Object System.Windows.Forms.Button
$themeBtn.Width = 44; $themeBtn.Height = 36
$themeBtn.Font = New-Object System.Drawing.Font('Segoe UI Symbol', 14)
$btnRow.Controls.Add($themeBtn)

function Set-Theme {
    $dk = $script:Dark
    $bg   = if ($dk) { [System.Drawing.Color]::FromArgb(32, 32, 32) }  else { [System.Drawing.SystemColors]::Control }
    $fg   = if ($dk) { [System.Drawing.Color]::FromArgb(235, 235, 235) } else { [System.Drawing.SystemColors]::ControlText }
    $box  = if ($dk) { [System.Drawing.Color]::FromArgb(45, 45, 45) }  else { [System.Drawing.SystemColors]::Window }
    $btn  = if ($dk) { [System.Drawing.Color]::FromArgb(60, 60, 60) }  else { [System.Drawing.SystemColors]::Control }
    foreach ($c in @($form, $hint, $linkRow, $btnRow, $iconBox)) { $c.BackColor = $bg; $c.ForeColor = $fg }
    foreach ($c in @($list, $linkBox)) { $c.BackColor = $box; $c.ForeColor = $fg }
    foreach ($b in @($upBtn, $downBtn, $removeBtn, $testBtn, $openBtn, $iconBtn, $saveBtn, $addBtn, $themeBtn)) {
        if ($dk) { $b.FlatStyle = 'Flat'; $b.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(90, 90, 90); $b.BackColor = $btn; $b.ForeColor = $fg }
        else { $b.FlatStyle = 'Standard'; $b.UseVisualStyleBackColor = $true; $b.ForeColor = $fg }
    }
    $themeBtn.Text = if ($dk) { [char]::ConvertFromUtf32(0x2600) } else { [char]::ConvertFromUtf32(0x1F319) }  # sun shows in dark, moon in light
    $tip.SetToolTip($themeBtn, $(if ($dk) { 'Switch to light mode' } else { 'Switch to dark mode' }))
    $v = [int]$dk
    if ($form.IsHandleCreated) { [void][LP.Native]::DwmSetWindowAttribute($form.Handle, 20, [ref]$v, 4) }  # dark title bar
}
$themeBtn.Add_Click({
    $script:Dark = -not $script:Dark
    try { [void](New-Item -ItemType Directory -Force (Split-Path $ThemeFile)); Set-Content $ThemeFile $(if ($script:Dark) { 'dark' } else { 'light' }) } catch {}
    Set-Theme
})
$form.Add_HandleCreated({ Set-Theme })

$tip = New-Object System.Windows.Forms.ToolTip
$tip.SetToolTip($iconBox, 'Click to go back to the first item''s icon')
$iconBox.Cursor = 'Hand'
$iconBox.Add_Click({ $script:IconLoc = $null; Update-IconPreview })
Set-Theme

Update-IconPreview
[void]$form.ShowDialog()
