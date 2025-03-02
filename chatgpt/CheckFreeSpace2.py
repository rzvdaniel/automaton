import shutil
import sys

def check_free_space(drive_letter):
    # Get the disk usage using shutil
    usage = shutil.disk_usage(drive_letter)
    free_space = usage.free  # Free space in bytes
    return free_space

if __name__ == '__main__':
    if len(sys.argv) != 2:
        print("Usage: python script.py <drive_letter>")
        sys.exit(1)
    drive = sys.argv[1]
    free_space = check_free_space(drive)
    print(f"Free space on {drive}: {free_space} bytes")