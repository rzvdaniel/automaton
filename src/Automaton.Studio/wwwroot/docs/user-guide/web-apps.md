# Web Apps

## Overview

Web Apps allow you to create **custom web interfaces** that execute your Automaton flows. Build interactive dashboards, data visualizations, forms, or any web-based tool using HTML, CSS, and JavaScript.

### Key Features

- ?? **Full HTML/CSS/JavaScript** - Use any framework (React, Vue, vanilla JS)
- ?? **Secure Execution** - Flows run with owner permissions, not viewer permissions
- ?? **Public or Private** - Choose who can access your app
- ?? **Simple API** - Execute flows with just a few lines of code
- ?? **Responsive** - Works on desktop, tablet, and mobile

### How It Works

```
???????????????????????????????????????
?  Your Web App (HTML/CSS/JS)        ?
?  Uses: automatonApi.executeFlow()  ?
???????????????????????????????????????
              ? Secure Communication
              ?
???????????????????????????????????????
?  Automaton Platform                 ?
?  - Validates permissions            ?
?  - Executes flow as app owner       ?
?  - Returns results                  ?
???????????????????????????????????????
```

---

## Creating a Web App

### 1. Create Your HTML

Navigate to **Apps** in the sidebar and click **New App**. Enter your HTML, CSS, and JavaScript:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>My First Web App</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            max-width: 800px;
            margin: 50px auto;
            padding: 20px;
        }
        button {
            background: #1890ff;
            color: white;
            border: none;
            padding: 10px 20px;
            border-radius: 4px;
            cursor: pointer;
        }
        button:hover {
            background: #40a9ff;
        }
    </style>
</head>
<body>
    <h1>Hello, Automaton!</h1>
    <p>Click the button to execute a flow:</p>
    <button onclick="executeMyFlow()">Run Flow</button>
    <div id="result"></div>

    <script>
        async function executeMyFlow() {
            try {
                const result = await automatonApi.executeFlow('MyFlowName', {
                    inputs: { name: 'World' }
                });
                
                document.getElementById('result').innerHTML = 
                    '<p>Success! Result: ' + JSON.stringify(result) + '</p>';
            } catch (error) {
                document.getElementById('result').innerHTML = 
                    '<p style="color: red;">Error: ' + error.message + '</p>';
            }
        }
    </script>
</body>
</html>
```

### 2. Configure Your App

- **Name**: Give your app a unique name
- **Display Name**: User-friendly name shown to viewers
- **Slug**: URL-friendly identifier (e.g., `my-app` ? `/web/my-app`)
- **Requires Authentication**: ? Check to require login, ? Uncheck for public access
- **Execution Runner**: (Optional) Specific runner to execute flows on

### 3. Publish Your App

Click **Publish** to make your app accessible at `/web/your-slug`.

---

## Automaton API Reference

The `automatonApi` object is **automatically available** in your web app HTML. No imports or setup required!

### API Methods

#### `executeFlow(flowIdentifier, options)`

Execute a flow by name or GUID.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `flowIdentifier` | `string` | ? | Flow name or GUID |
| `options` | `object` | ? | Execution options |
| `options.inputs` | `object` | ? | Input variables (key-value pairs) |
| `options.runnerNames` | `string[]` | ? | Specific runners to use |

**Returns:** `Promise<object>` - Flow execution result

**Example:**

```javascript
// Execute flow by name
const result = await automatonApi.executeFlow('GetWeather', {
    inputs: { city: 'New York' }
});

console.log(result.OutputVariables); // { temperature: "72°F", ... }
```

---

#### `customCall(path, method, body)`

Make a custom API call (advanced usage).

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | `string` | ? | API path (must start with `/api/web-apps/`) |
| `method` | `string` | ? | HTTP method (`GET` or `POST`) |
| `body` | `object` | ? | Request body |

**Returns:** `Promise<object>` - API response

**Example:**

```javascript
const result = await automatonApi.customCall(
    '/api/web-apps/my-app/flows/MyFlow/execute',
    'POST',
    { inputs: { key: 'value' } }
);
```

---

#### `getAppSlug()`

Get the current app's URL slug.

**Returns:** `string` - The app slug

**Example:**

```javascript
const slug = automatonApi.getAppSlug();
console.log(slug); // "my-app"
```

---

## Complete Examples

### Example 1: Simple Data Fetcher

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>Data Fetcher</title>
    <style>
        body { font-family: Arial; max-width: 600px; margin: 50px auto; }
        .loading { color: #666; font-style: italic; }
        .error { color: red; padding: 10px; background: #fee; border-radius: 4px; }
        .result { padding: 10px; background: #efe; border-radius: 4px; }
    </style>
</head>
<body>
    <h1>Data Fetcher</h1>
    <button onclick="fetchData()">Fetch Data</button>
    <div id="output"></div>

    <script>
        async function fetchData() {
            const output = document.getElementById('output');
            output.innerHTML = '<p class="loading">Loading...</p>';
            
            try {
                const result = await automatonApi.executeFlow('FetchData');
                
                output.innerHTML = `
                    <div class="result">
                        <h3>Success!</h3>
                        <pre>${JSON.stringify(result.OutputVariables, null, 2)}</pre>
                    </div>
                `;
            } catch (error) {
                output.innerHTML = `
                    <div class="error">
                        <strong>Error:</strong> ${error.message}
                    </div>
                `;
            }
        }
    </script>
</body>
</html>
```

---

### Example 2: Form with Input Variables

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>User Search</title>
    <style>
        body { font-family: Arial; max-width: 600px; margin: 50px auto; }
        input { padding: 8px; width: 300px; margin-right: 10px; }
        button { padding: 8px 16px; background: #1890ff; color: white; border: none; cursor: pointer; }
        .results { margin-top: 20px; }
        .user-card { padding: 15px; background: #f5f5f5; border-radius: 4px; margin: 10px 0; }
    </style>
</head>
<body>
    <h1>User Search</h1>
    <form onsubmit="searchUsers(event)">
        <input type="text" id="searchTerm" placeholder="Enter search term..." required>
        <button type="submit">Search</button>
    </form>
    <div id="results" class="results"></div>

    <script>
        async function searchUsers(event) {
            event.preventDefault();
            
            const searchTerm = document.getElementById('searchTerm').value;
            const results = document.getElementById('results');
            
            results.innerHTML = '<p>Searching...</p>';
            
            try {
                const result = await automatonApi.executeFlow('SearchUsers', {
                    inputs: { 
                        query: searchTerm,
                        limit: '10'
                    }
                });
                
                const users = JSON.parse(result.OutputVariables.users || '[]');
                
                if (users.length === 0) {
                    results.innerHTML = '<p>No users found.</p>';
                    return;
                }
                
                results.innerHTML = users.map(user => `
                    <div class="user-card">
                        <strong>${user.name}</strong><br>
                        <small>${user.email}</small>
                    </div>
                `).join('');
                
            } catch (error) {
                results.innerHTML = `<p style="color: red;">Error: ${error.message}</p>`;
            }
        }
    </script>
</body>
</html>
```

---

### Example 3: Real-Time Dashboard (using React CDN)

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>Dashboard</title>
    <script crossorigin src="https://unpkg.com/react@18/umd/react.production.min.js"></script>
    <script crossorigin src="https://unpkg.com/react-dom@18/umd/react-dom.production.min.js"></script>
    <script src="https://unpkg.com/@babel/standalone/babel.min.js"></script>
    <style>
        body { font-family: Arial; margin: 20px; }
        .dashboard { display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 20px; }
        .card { padding: 20px; background: white; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }
        .metric { font-size: 32px; font-weight: bold; color: #1890ff; }
        button { padding: 10px 20px; background: #1890ff; color: white; border: none; border-radius: 4px; cursor: pointer; }
    </style>
</head>
<body>
    <div id="root"></div>

    <script type="text/babel">
        const { useState, useEffect } = React;

        function Dashboard() {
            const [data, setData] = useState(null);
            const [loading, setLoading] = useState(false);
            const [error, setError] = useState(null);

            const fetchDashboard = async () => {
                setLoading(true);
                setError(null);
                
                try {
                    const result = await automatonApi.executeFlow('GetDashboardMetrics');
                    setData(result.OutputVariables);
                } catch (err) {
                    setError(err.message);
                } finally {
                    setLoading(false);
                }
            };

            useEffect(() => {
                fetchDashboard();
                const interval = setInterval(fetchDashboard, 30000); // Refresh every 30s
                return () => clearInterval(interval);
            }, []);

            if (error) return <div style={{color: 'red'}}>Error: {error}</div>;
            if (loading && !data) return <div>Loading...</div>;

            return (
                <div>
                    <h1>Dashboard</h1>
                    <button onClick={fetchDashboard} disabled={loading}>
                        {loading ? 'Refreshing...' : 'Refresh'}
                    </button>
                    
                    {data && (
                        <div className="dashboard" style={{marginTop: '20px'}}>
                            <div className="card">
                                <div>Total Users</div>
                                <div className="metric">{data.totalUsers}</div>
                            </div>
                            <div className="card">
                                <div>Active Sessions</div>
                                <div className="metric">{data.activeSessions}</div>
                            </div>
                            <div className="card">
                                <div>Revenue</div>
                                <div className="metric">${data.revenue}</div>
                            </div>
                        </div>
                    )}
                </div>
            );
        }

        ReactDOM.render(<Dashboard />, document.getElementById('root'));
    </script>
</body>
</html>
```

---

## Security & Best Practices

### ? DO

- ? Use `automatonApi.executeFlow()` for all flow executions
- ? Handle errors gracefully with try/catch
- ? Validate user input before sending to flows
- ? Use `RequiresAuthentication` for sensitive apps
- ? Test your app thoroughly before publishing

### ? DON'T

- ? Don't try to bypass `automatonApi` with direct `fetch()` calls (won't work - security enforced)
- ? Don't expose sensitive data in your HTML
- ? Don't assume flows will always succeed - handle errors
- ? Don't hardcode credentials in your app HTML

---

## Security Model

### How Authentication Works

**Public Apps** (`RequiresAuthentication = false`):
- ? Anyone can access at `/web/your-slug`
- ? Flows execute as the **app owner** (not the viewer)
- ? No login required

**Protected Apps** (`RequiresAuthentication = true`):
- ? Viewers must log in to Automaton
- ? Flows execute as the **app owner** (not the viewer)
- ? JWT token automatically included

### Security Layers

Your web apps are protected by **multiple security layers**:

1. **Origin Validation** - Only same-origin iframes accepted
2. **Path Whitelist** - Only `/api/web-apps/*` endpoints allowed
3. **Owner-Based Execution** - Flows run with app owner's permissions
4. **App-Level Authentication** - Optional login requirement
5. **Payload Validation** - Request structure validated before processing

**What this means:**
- ? Viewers cannot access other apps' flows
- ? Viewers cannot access flows they don't own
- ? XSS attempts are contained within the app's scope
- ? No privilege escalation possible

---

## Using External Libraries

You have **full freedom** to use any JavaScript library or framework:

### jQuery

```html
<script src="https://code.jquery.com/jquery-3.6.0.min.js"></script>
<script>
    $('#myButton').click(async function() {
        const result = await automatonApi.executeFlow('MyFlow');
        $('#output').text(JSON.stringify(result));
    });
</script>
```

### Axios (for external APIs)

```html
<script src="https://cdn.jsdelivr.net/npm/axios/dist/axios.min.js"></script>
<script>
    // ? Use Axios for external APIs
    const weatherData = await axios.get('https://api.weather.com/...');
    
    // ? Use automatonApi for Automaton flows
    const result = await automatonApi.executeFlow('SaveWeather', {
        inputs: { data: JSON.stringify(weatherData.data) }
    });
</script>
```

### Chart.js

```html
<script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
<canvas id="myChart"></canvas>
<script>
    const result = await automatonApi.executeFlow('GetChartData');
    const chartData = JSON.parse(result.OutputVariables.chartData);
    
    new Chart(document.getElementById('myChart'), {
        type: 'bar',
        data: chartData
    });
</script>
```

---

## Troubleshooting

### Flow Not Executing

**Error:** `Request timeout after 5 seconds`

**Causes:**
1. Flow name is incorrect (case-sensitive)
2. Flow doesn't exist in your account
3. Runner is offline

**Solution:**
- Verify flow name in **Flows** page
- Ensure at least one runner is online
- Check browser console for detailed error messages

---

### Authentication Issues

**Error:** `This app requires authentication`

**Solution:**
- Your app has `RequiresAuthentication = true`
- Viewers must log in at `/account/login`
- Or: Change app settings to allow public access

---

### Input Variables Not Working

**Problem:** Flow executes but doesn't use inputs

**Solution:**
- Ensure flow has matching **Input Variables** defined
- Variable names are case-sensitive
- Pass inputs as strings: `{ count: '10' }` not `{ count: 10 }`

---

## API Response Format

### Successful Execution

```json
{
  "Success": true,
  "FlowId": "123e4567-e89b-12d3-a456-426614174000",
  "Status": "Success",
  "RunnerCount": 1,
  "ExecutedAt": "2024-01-15T10:30:00Z",
  "Message": "Flow executed successfully",
  "OutputVariables": {
    "result": "Hello, World!",
    "count": "42"
  },
  "Logs": [
    "Step 1 completed",
    "Step 2 completed"
  ]
}
```

### Failed Execution

```json
{
  "Success": false,
  "FlowId": "123e4567-e89b-12d3-a456-426614174000",
  "Status": "Failed",
  "Message": "Flow execution failed or incomplete",
  "error": "Network error"
}
```

---

## FAQ

**Q: Can I use TypeScript?**  
A: Yes! Use a build tool (webpack, vite) locally, then paste the compiled JavaScript into your app HTML.

**Q: Can I make API calls to external services?**  
A: Yes! Use `fetch()`, `axios`, or any library for external APIs. Use `automatonApi` only for Automaton flows.

**Q: Can I use React/Vue/Angular?**  
A: Yes! Either include from CDN or build locally and paste the compiled output.

**Q: How do I debug my app?**  
A: Open browser DevTools (F12) while viewing your app. `console.log()` works normally.

**Q: Can I use my own domain?**  
A: Not yet - apps are always hosted at `/web/your-slug`. Custom domains coming soon!

**Q: Is there a size limit for app HTML?**  
A: No hard limit, but keep it reasonable (< 1MB). For large apps, consider external hosting and iframe embedding.

---

## Next Steps

- ?? [Working with Flows](flows.md) - Learn to create flows for your apps
- ?? [Managing Runners](runners.md) - Set up runners to execute flows
- ?? [Authentication Guide](../getting-started.md#authentication) - Secure your apps

---

**Need Help?** Check the [Quick Start Guide](../getting-started.md) or contact support.
