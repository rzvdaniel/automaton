import openai
import datetime
from openai import OpenAI

# Inițializare client OpenAI - ia cheia proprie de aici: 
# https://openai.com/api/
client = OpenAI(api_key="")

# O funcție care returnează data calculatorului - atenție data poate fi greșită, dacă ceasul computerului nu e setat corect. 
# Next tutorial, cum să apelezi un API extern care dă timpul.

def get_current_datetime():
    now = datetime.datetime.now()
    return {
        "date": now.strftime("%Y-%m-%d"),
        "time": now.strftime("%H:%M:%S"),
        "day": now.strftime("%A"),
        "month_days": (datetime.date(now.year, now.month + 1, 1) - datetime.timedelta(days=1)).day
    }

# Inregistrarea funcției/toolului pentru ChatGPT - Atenție folosirea funcției depunde de specificitatea descrierii

tools = [
    {
        "type": "function",
        "function": {
            "name": "get_current_datetime",
            "description": "Returnează data și ora actuală, numele zilei și numărul de zile din lună.",
            "parameters": {
                "type": "object",
                "properties": {}
            }
        }
    }
]

# Mecanismul de răspuns - GPT decide ce să folosească

def ask_gpt(user_question):
    messages = [{"role": "user", "content": user_question}]
    
    print(f"Trimit întrebarea: {user_question}")
    
    response = client.chat.completions.create(
        model="gpt-4",
        messages=messages,
        tools=tools,
        tool_choice="auto"
    )
    
    response_message = response.choices[0].message
    
    if response_message.tool_calls:
        print("GPT a decis să apeleze o funcție!")
        tool_call = response_message.tool_calls[0]
        print(f"Funcția aleasă: {tool_call.function.name}")
        
        if tool_call.function.name == "get_current_datetime":
            function_response = get_current_datetime()
            print(f"Rezultatul funcției: {function_response}")
            
            messages.append(response_message)
            messages.append({
                "role": "tool",
                "tool_call_id": tool_call.id,
                "name": "get_current_datetime",
                "content": str(function_response)
            })
            
            print("Cer un nou răspuns cu datele obținute")
            response = client.chat.completions.create(
                model="gpt-4",
                messages=messages
            )
            
            return response.choices[0].message.content
    else:
        print("GPT a decis să răspundă direct, fără a apela vreo funcție")
    
    return response_message.content

# Exemple de întrebări (cu și fără necesitatea de a știi data curentă):

questions = [
    "Care este data de azi?",
    "Care este data nasterii lui Ion Iliescu?",
    "Cate zile au trecut de la nașterea lui Napoleon?",
    "Care este luna curentă",
    "Când apune soarele astăzi în București",
    "Câte zile mai sunt din luna aceasta?",
    "Este weekend?"
]

# Rulăm întrebările
for q in questions:
    print(f"🔹 Întrebare: {q}")
    print(f"💬 Răspuns: {ask_gpt(q)}\n")