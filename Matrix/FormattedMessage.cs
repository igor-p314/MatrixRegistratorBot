using System.Collections.Generic;

namespace MatrixRegistratorBot.Matrix
{
    internal sealed record FormattedMessage : Message
    {
        private readonly string _formattedMessageText;

        public FormattedMessage(string formattedMessageText, string messageText) : base(messageText)
        {
            _formattedMessageText = formattedMessageText;
        }

        public override Dictionary<string, string> ToSerializableMessage()
        {
            return new Dictionary<string, string>
            {
                { "msgtype", "m.text" },
                { "body", _messageText },
                { "format", "org.matrix.custom.html" },
                { "formatted_body", _formattedMessageText },
            };
        }
    }
}
