# This now works perfectly! 🎉
import requests
import pandas as pd
import numpy as np

# Modern Python 3.7+ syntax
response = requests.get('https://api.github.com/users/octocat')
data = response.json()
message = f"User {data['login']} has {data['public_repos']} repos!"

print (message)
# Use any Python package that's installed