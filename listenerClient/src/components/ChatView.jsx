import { useState, useRef, useEffect } from 'react';
import { Send } from 'lucide-react';
import { sendChatMessage } from '../api/chat';
import ChatMessage from './ChatMessage';
import ChatIdentityBar from './ChatIdentityBar';

const WELCOME_MESSAGE = {
  role: 'assistant',
  content:
    "Hi! I'm your **EWA Assistant**. I can help you check your earnings, deductions, tax info, and time entries.\n\nSelect your name above to get started, then ask me anything!",
  timestamp: new Date(),
};

const QUICK_ACTIONS = [
  "What's my pay info?",
  'Show my deductions',
  'What are my hours?',
  'Check my tax withholdings',
];

export default function ChatView() {
  const [messages, setMessages] = useState([WELCOME_MESSAGE]);
  const [conversationHistory, setConversationHistory] = useState([]);
  const [inputValue, setInputValue] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [employeeName, setEmployeeName] = useState('');
  const [error, setError] = useState(null);
  const messagesEndRef = useRef(null);
  const inputRef = useRef(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  const handleSend = async (text) => {
    const messageText = text || inputValue.trim();
    if (!messageText || isLoading) return;

    if (!employeeName) {
      setError('Please select an employee name above before chatting.');
      return;
    }

    setError(null);
    setInputValue('');

    const userMessage = {
      role: 'employee',
      content: messageText,
      timestamp: new Date(),
    };
    setMessages((prev) => [...prev, userMessage]);
    setIsLoading(true);

    try {
      const outgoingMessage = `As ${employeeName}, ${messageText}`;
      const data = await sendChatMessage(outgoingMessage, conversationHistory);

      const assistantMessage = {
        role: 'assistant',
        content: data.response,
        timestamp: new Date(),
      };
      setMessages((prev) => [...prev, assistantMessage]);

      if (data.conversationHistory) {
        setConversationHistory(data.conversationHistory);
      }
    } catch (err) {
      const errorMessage = {
        role: 'assistant',
        content: "Sorry, I couldn't reach the server. Please try again.",
        timestamp: new Date(),
      };
      setMessages((prev) => [...prev, errorMessage]);
    } finally {
      setIsLoading(false);
      inputRef.current?.focus();
    }
  };

  const handleKeyDown = (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  return (
    <div className="chat-container">
      <ChatIdentityBar employeeName={employeeName} onNameChange={setEmployeeName} />

      <div className="chat-messages">
        {messages.map((msg, i) => (
          <ChatMessage key={i} role={msg.role} content={msg.content} timestamp={msg.timestamp} />
        ))}

        {isLoading && (
          <div className="chat-message chat-message-assistant">
            <div className="chat-bubble chat-bubble-assistant">
              <div className="chat-typing">
                <span></span><span></span><span></span>
              </div>
            </div>
          </div>
        )}

        <div ref={messagesEndRef} />
      </div>

      {messages.length === 1 && (
        <div className="chat-quick-actions">
          {QUICK_ACTIONS.map((action) => (
            <button
              key={action}
              className="chat-quick-btn"
              onClick={() => handleSend(action)}
              disabled={!employeeName}
            >
              {action}
            </button>
          ))}
        </div>
      )}

      {error && <div className="chat-error">{error}</div>}

      <div className="chat-input-bar">
        <input
          ref={inputRef}
          className="chat-input"
          type="text"
          placeholder={employeeName ? 'Type your message...' : 'Select an employee first...'}
          value={inputValue}
          onChange={(e) => setInputValue(e.target.value)}
          onKeyDown={handleKeyDown}
          disabled={isLoading || !employeeName}
          maxLength={2000}
        />
        <button
          className="chat-send-btn"
          onClick={() => handleSend()}
          disabled={isLoading || !inputValue.trim() || !employeeName}
        >
          <Send size={18} />
        </button>
      </div>
    </div>
  );
}
