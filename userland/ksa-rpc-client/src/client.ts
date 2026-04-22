import { connect } from "bun";
import type { RpcRequest, RpcResponse, ClientOptions } from "./types";

/**
 * RPC client for communicating with Kitten Space Agency game via TCP
 */
export class KsaRpcClient {
  private host: string;
  private port: number;
  private timeout: number;

  /**
   * Create a new KSA RPC client
   * @param target - Target endpoint in "host:port" format (defaults to KSA_RPC_TARGET env var)
   * @param options - Client configuration options
   */
  constructor(target?: string, options?: ClientOptions) {
    const targetStr = target ?? process.env.KSA_RPC_TARGET ?? "";
    this.timeout = options?.timeout ?? 5000;

    if (!targetStr) {
      throw new Error(
        "Target not provided. Set KSA_RPC_TARGET environment variable or pass target to constructor."
      );
    }

    // Parse host:port
    const parts = targetStr.split(":");
    if (parts.length !== 2) {
      throw new Error(
        `Invalid target format "${targetStr}". Expected "host:port" format (e.g., "localhost:4242")`
      );
    }

    this.host = parts[0];
    const parsedPort = parseInt(parts[1], 10);
    if (isNaN(parsedPort) || parsedPort <= 0 || parsedPort > 65535) {
      throw new Error(`Invalid port number: ${parts[1]}`);
    }
    this.port = parsedPort;
  }

  /**
   * Call an RPC action on the game server
   * @param action - Action name to invoke
   * @param params - Optional parameters for the action
   * @returns Promise resolving to the response data
   * @throws Error if the request fails or times out
   */
  async call<T = unknown>(
    action: string,
    params?: Record<string, unknown>
  ): Promise<T> {
    const request: RpcRequest = { action, params };
    const requestJson = JSON.stringify(request) + "\n";

    return new Promise<T>(async (resolve, reject) => {
      const timeoutId = setTimeout(() => {
        reject(new Error(`Request timed out after ${this.timeout}ms`));
      }, this.timeout);

      // Accumulate data chunks until we receive a complete message (terminated by \n)
      let responseBuffer = Buffer.alloc(0);

      try {
        // Connect to TCP socket
        const socket = await connect({
          hostname: this.host,
          port: this.port,
          socket: {
            data(socket, data) {
              // Accumulate data
              responseBuffer = Buffer.concat([responseBuffer, Buffer.from(data)]);

              // Check if we have a complete message (ends with newline)
              const responseText = responseBuffer.toString("utf-8");
              const newlineIndex = responseText.indexOf("\n");

              if (newlineIndex !== -1) {
                clearTimeout(timeoutId);

                // Extract complete message (everything before newline)
                let completeMessage = responseText.substring(0, newlineIndex);

                // Strip UTF-8 BOM if present (EF BB BF)
                if (completeMessage.charCodeAt(0) === 0xfeff) {
                  completeMessage = completeMessage.substring(1);
                }

                completeMessage = completeMessage.trim();

                try {
                  const response: RpcResponse<T> = JSON.parse(completeMessage);

                  if (response.success) {
                    resolve(response.data as T);
                  } else {
                    reject(new Error(response.error ?? "Unknown error"));
                  }
                } catch (parseError) {
                  reject(
                    new Error(
                      `Failed to parse response: ${parseError instanceof Error ? parseError.message : String(parseError)}`
                    )
                  );
                }

                // Close socket after receiving complete response
                socket.end();
              }
              // Otherwise, keep accumulating data for next callback
            },
            error(socket, error) {
              clearTimeout(timeoutId);
              reject(error);
            },
            close(socket) {
              clearTimeout(timeoutId);
            },
          },
        });

        // Send request
        socket.write(requestJson);
      } catch (error) {
        clearTimeout(timeoutId);
        reject(
          new Error(
            `Failed to connect to socket: ${error instanceof Error ? error.message : String(error)}`
          )
        );
      }
    });
  }

  /**
   * Get the configured target endpoint
   */
  getTarget(): string {
    return `${this.host}:${this.port}`;
  }
}
