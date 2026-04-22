```bash
script -q cmd.out
# run your app inside the session:
your-command --args
exit
```

```bash
# hex view (shows 1b for ESC, etc.)
xxd -g 1 cmd.out | less

# or a classic hexdump
hexdump -C cmd.out | less
```

```bash
python3 - <<'PY'
data = open("cmd.out","rb").read()
# show ESC as <ESC>, CR/LF as tokens; keep everything else as-is where possible
s = data.decode("utf-8", "backslashreplace")
s = (s.replace("\x1b", "<ESC>")
       .replace("\r", "<CR>\n")
       .replace("\n", "<LF>\n"))
print(s)
PY
```

```bash
asciinema rec --raw out.cast
# run your program, then exit
```