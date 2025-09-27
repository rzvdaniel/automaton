import requests

def fetch_weather(city, country):
    """
    Fetches the weather for a given city and country using a public API.

    Args:
        city (str): The name of the city.
        country (str): The name of the country.

    Returns:
        str: The weather description or an error message.
    """
    # Base URL of the public weather API that does not require an API key
    base_url = "http://wttr.in"

    # Construct the request URL
    request_url = f"{base_url}/{city},{country}?format=%C"

    try:
        # Make the GET request to fetch the weather
        response = requests.get(request_url)
        
        # Raise an HTTPError for bad responses (4XX or 5XX)
        response.raise_for_status()

        # Extract the weather information from the response
        weather = response.text.strip()

        # Return the weather description
        return weather

    except requests.exceptions.HTTPError as http_err:
        return f"HTTP error occurred: {http_err}"
    except requests.exceptions.ConnectionError as conn_err:
        return f"Connection error occurred: {conn_err}"
    except requests.exceptions.Timeout as timeout_err:
        return f"Timeout occurred: {timeout_err}"
    except requests.exceptions.RequestException as req_err:
        return f"An error occurred: {req_err}"

# Input Variables
country = "Spain"
city = "Madrid"

# Fetch weather and assign to output variable
weather = fetch_weather(city, country)

print(f"The weather in {city}, {country} is: {weather}")