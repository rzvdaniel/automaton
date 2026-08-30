import os
import shutil

os.makedirs("out", exist_ok=True)
shutil.copyfile("server.py", os.path.join("out", "server.py"))
with open(os.path.join("out", "build-marker.txt"), "w", encoding="utf-8") as marker:
    marker.write("python-build-completed\n")
