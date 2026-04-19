# CSI

## 1001 - engine ignite

echo -ne '\e[>1001;1F'

### PowerShell (paste into Windows Terminal / PowerShell)

```powershell
$S=[Console]::OpenStandardOutput();$S.WriteByte(0x1b);$b=[Text.Encoding]::ASCII.GetBytes("[>1001;1F");$S.Write($b,0,$b.Length);$S.Flush()
```

## 1002 - engine shutdown

echo -ne '\e[>1002;1F'

### PowerShell (paste into Windows Terminal / PowerShell)

```powershell
$S=[Console]::OpenStandardOutput();$S.WriteByte(0x1b);$b=[Text.Encoding]::ASCII.GetBytes("[>1002;1F");$S.Write($b,0,$b.Length);$S.Flush()
```

# OSC

## 1010 - arbitrary JSON payload

### Shortest form (echo -ne only, bash/zsh)
echo -ne '\e]1010;{"action":"engine_ignite"}\a'
echo -ne '\e]1010;{"action":"engine_shutdown"}\a'

### PowerShell (paste into Windows Terminal / PowerShell)

```powershell
$S=[Console]::OpenStandardOutput();$S.WriteByte(0x1b);$b=[Text.Encoding]::ASCII.GetBytes(']1010;{"action":"engine_ignite"}');$S.Write($b,0,$b.Length);$S.WriteByte(0x07);$S.Flush()
```

```powershell
$S=[Console]::OpenStandardOutput();$S.WriteByte(0x1b);$b=[Text.Encoding]::ASCII.GetBytes(']1010;{"action":"engine_shutdown"}');$S.Write($b,0,$b.Length);$S.WriteByte(0x07);$S.Flush()
```

### Hex notation (works in printf and echo)
printf '\x1b]1010;{"action":"engine_ignite"}\a'
printf '\x1b]1010;{"action":"engine_shutdown"}\a'

### Octal notation (works everywhere)
printf '\033]1010;{"action":"engine_ignite"}\a'
printf '\033]1010;{"action":"engine_shutdown"}\a'
